"""Loopback-only model generation UI. One worker, explicit cancellation, no uploads to cloud."""
import argparse
import base64
from datetime import datetime
import io
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import secrets
import socket
import subprocess
import sys
import threading
import time
from urllib.parse import urlparse
import webbrowser

ROOT = Path(__file__).resolve().parent
TOKEN = secrets.token_urlsafe(32)
LOCK = threading.Lock()
CURRENT = None
LAST_DIRECTORY = None


class LocalServer(ThreadingHTTPServer):
    # Windows SO_REUSEADDR permits two listeners on one port. Exclusive binding
    # prevents two local generation services from consuming RAM concurrently.
    allow_reuse_address = False

    def server_bind(self):
        if hasattr(socket, 'SO_EXCLUSIVEADDRUSE'):
            self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        super().server_bind()


def terminate_worker(job, reason):
    import psutil
    job['reason'] = reason
    try:
        parent = psutil.Process(job['process'].pid)
        children = parent.children(recursive=True)
        for process in reversed(children):
            try:
                process.terminate()
            except psutil.NoSuchProcess:
                pass
        parent.terminate()
    except psutil.NoSuchProcess:
        pass


def job_status():
    if CURRENT is None:
        if LAST_DIRECTORY is not None:
            result = json.loads((LAST_DIRECTORY / 'run.json').read_text(encoding='utf-8'))
            result.update(id=LAST_DIRECTORY.name, folder=str(LAST_DIRECTORY), running=False,
                          files=['model.obj', 'model.glb', 'run.json'])
            return apply_quality_status(result, LAST_DIRECTORY)
        return {'status': 'idle'}
    directory, process = CURRENT['directory'], CURRENT['process']
    result = {'status': 'starting', 'id': directory.name, 'folder': str(directory)}
    record = directory / 'run.json'
    if record.exists():
        try:
            result.update(json.loads(record.read_text(encoding='utf-8')))
        except (ValueError, PermissionError):
            pass
    code = process.poll()
    if code is not None and result.get('status') not in ('completed', 'failed'):
        result.update(status='failed', error=CURRENT.get('reason', f'Worker exited with code {code}'))
    log = directory / 'console.log'
    if log.exists():
        with log.open('rb') as stream:
            stream.seek(max(log.stat().st_size - 12000, 0))
            result['log'] = stream.read().decode('utf-8', errors='replace')
    result['running'] = code is None
    result['files'] = [name for name in ('model.obj', 'model.glb', 'run.json') if (directory / name).exists()]
    return apply_quality_status(result, directory)


def apply_quality_status(result, directory):
    audit = directory / 'mesh_quality.json'
    if audit.is_file():
        quality = json.loads(audit.read_text(encoding='utf-8'))
        result['quality'] = quality
        if not quality['passed'] and result.get('status') == 'completed':
            result.update(status='failed', error=quality['reason'])
    return result


def monitor(job):
    import psutil
    low_memory_ticks = 0
    while job['process'].poll() is None:
        low_memory_ticks = low_memory_ticks + 1 if psutil.virtual_memory().available < 768 * 1024 ** 2 else 0
        if low_memory_ticks >= 3:
            terminate_worker(job, 'RAM safety stop: less than 768 MiB system RAM available. Close Unity/Blender and retry.')
            break
        time.sleep(1)
    job['process'].wait()
    job['log'].close()
    record = job['directory'] / 'run.json'
    if record.exists():
        data = json.loads(record.read_text(encoding='utf-8'))
        if data.get('status') == 'running':
            data.update(status='failed', error=job.get('reason',
                f"Worker exited with code {job['process'].returncode}"))
            temp = record.with_suffix('.monitor.tmp')
            temp.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
            temp.replace(record)


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        pass

    def valid_host(self):
        return self.headers.get('Host') == f'127.0.0.1:{self.server.server_port}'

    def send(self, content, content_type='application/json; charset=utf-8', status=200):
        if isinstance(content, dict):
            content = json.dumps(content, ensure_ascii=False).encode('utf-8')
        if isinstance(content, str):
            content = content.encode('utf-8')
        self.send_response(status)
        self.send_header('Content-Type', content_type)
        self.send_header('Content-Length', str(len(content)))
        self.send_header('Cache-Control', 'no-store')
        self.send_header('X-Content-Type-Options', 'nosniff')
        self.send_header('Content-Security-Policy', "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self'; frame-ancestors 'none'")
        self.end_headers()
        self.wfile.write(content)

    def do_GET(self):
        if not self.valid_host():
            return self.send({'error': 'Invalid host'}, status=403)
        path = urlparse(self.path).path
        if path == '/':
            return self.send((ROOT / 'ui.html').read_text(encoding='utf-8').replace('__TOKEN__', TOKEN), 'text/html; charset=utf-8')
        if path == '/api/status':
            with LOCK:
                result = job_status()
            result['weights_ready'] = (ROOT / '.runtime/weights/verified.json').exists()
            result['service'] = 'MRLab.Hunyuan3D.Local'
            return self.send(result)
        if path == '/example.png':
            return self.send((ROOT / 'Examples/official_demo.png').read_bytes(), 'image/png')
        output = CURRENT['directory'] if CURRENT else LAST_DIRECTORY
        if path.startswith('/download/') and output:
            name = path.rsplit('/', 1)[-1]
            if name in ('model.obj', 'model.glb', 'run.json', 'prepared_preview.png', 'prepared.png', 'foreground_mask.png'):
                file = output / name
                if file.is_file():
                    return self.send(file.read_bytes(), 'image/png' if name.endswith('.png') else 'application/octet-stream')
        return self.send({'error': 'Not found'}, status=404)

    def do_POST(self):
        global CURRENT
        if not self.valid_host() or not secrets.compare_digest(self.headers.get('X-Local-Token', ''), TOKEN):
            return self.send({'error': 'Invalid local token. Reload this page.'}, status=403)
        try:
            length = int(self.headers.get('Content-Length', '0'))
            if not 0 < length <= 22 * 1024 ** 2:
                raise ValueError('Request must be smaller than 22 MiB.')
            body = json.loads(self.rfile.read(length))
            if self.path == '/api/cancel':
                with LOCK:
                    if CURRENT and CURRENT['process'].poll() is None:
                        terminate_worker(CURRENT, 'Cancelled by user. Inputs and logs preserved.')
                return self.send({'ok': True})
            if self.path == '/api/shutdown':
                with LOCK:
                    if CURRENT and CURRENT['process'].poll() is None:
                        terminate_worker(CURRENT, 'Local service stopped. Inputs and logs preserved.')
                self.send({'ok': True})
                threading.Thread(target=self.server.shutdown, daemon=True).start()
                return
            if self.path != '/api/generate':
                return self.send({'error': 'Not found'}, status=404)
            from PIL import Image
            encoded = body.get('image', '')
            raw = base64.b64decode(encoded, validate=True)
            if len(raw) > 15 * 1024 ** 2:
                raise ValueError('Image must be smaller than 15 MiB.')
            with Image.open(io.BytesIO(raw)) as source:
                if source.width * source.height > 24_000_000:
                    raise ValueError('Image exceeds 24 megapixels.')
                image = source.convert('RGBA')
            steps, resolution = int(body.get('steps', 20)), int(body.get('resolution', 128))
            backend, seed = body.get('backend', 'directml'), int(body.get('seed', 1234))
            if not 2 <= steps <= 60 or resolution not in (64, 96, 128, 192, 256) or backend not in ('directml', 'cpu'):
                raise ValueError('Invalid generation settings.')
            with LOCK:
                if CURRENT and CURRENT['process'].poll() is None:
                    return self.send({'error': 'A generation is already running.'}, status=409)
                if not (ROOT / '.runtime/weights/verified.json').exists():
                    raise ValueError('Weights not ready. Run setup.ps1 first.')
                directory = ROOT / 'Outputs' / (datetime.now().strftime('%Y%m%d_%H%M%S_') + secrets.token_hex(3))
                directory.mkdir(parents=True)
                reference = directory / 'reference.png'
                image.save(reference)
                (directory / 'original_input.bin').write_bytes(raw)
                log = (directory / 'console.log').open('wb')
                command = [sys.executable, '-u', str(ROOT / 'generate.py'), '--input', str(reference),
                    '--output', str(directory), '--steps', str(steps), '--resolution', str(resolution),
                    '--seed', str(seed), '--backend', backend]
                try:
                    process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT,
                        cwd=ROOT, creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0),
                        env={**__import__('os').environ, 'PYTHONUTF8': '1', 'PYTHONIOENCODING': 'utf-8'})
                except Exception:
                    log.close()
                    raise
                CURRENT = {'directory': directory, 'process': process, 'log': log}
                threading.Thread(target=monitor, args=(CURRENT,), daemon=True).start()
            return self.send({'ok': True, 'id': directory.name})
        except Exception as error:
            return self.send({'error': f'{type(error).__name__}: {error}'}, status=400)


def main():
    global LAST_DIRECTORY
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', type=int, default=7861)
    parser.add_argument('--no-browser', action='store_true')
    args = parser.parse_args()
    try:
        server = LocalServer(('127.0.0.1', args.port), Handler)
    except OSError:
        import requests
        url = f'http://127.0.0.1:{args.port}'
        existing = requests.get(url + '/api/status', timeout=3).json()
        if existing.get('service') != 'MRLab.Hunyuan3D.Local':
            raise RuntimeError('Port is occupied by another service; choose another port or stop it.')
        print('Reusing existing local service: ' + url, flush=True)
        if not args.no_browser:
            webbrowser.open(url)
        return
    for record in sorted((ROOT / 'Outputs').glob('*/run.json'), reverse=True):
        try:
            data = json.loads(record.read_text(encoding='utf-8'))
            if data.get('status') == 'completed' and all((record.parent / name).is_file()
                    for name in ('model.obj', 'model.glb')):
                LAST_DIRECTORY = record.parent
                break
        except (ValueError, OSError):
            continue
    server.daemon_threads = True
    url = f'http://127.0.0.1:{server.server_port}'
    print('Hunyuan3D local UI: ' + url, flush=True)
    print('Keep this window open. Ctrl+C stops the service and its active worker.', flush=True)
    if not args.no_browser:
        webbrowser.open(url)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        if CURRENT and CURRENT['process'].poll() is None:
            terminate_worker(CURRENT, 'Local service stopped.')
            CURRENT['process'].wait(timeout=15)
        server.server_close()


if __name__ == '__main__':
    main()
