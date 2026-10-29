@echo off
setlocal
cd /d "%~dp0"
set "TOOL=%~dp0SourceAssets\Models\Hunyuan3D"
set "PYTHON=%TOOL%\.runtime\venv\Scripts\python.exe"
if not exist "%PYTHON%" goto setup
if not exist "%TOOL%\.runtime\weights\verified.json" goto setup
"%PYTHON%" -c "import importlib.util,sys; sys.exit(0 if importlib.util.find_spec('rembg') and importlib.util.find_spec('onnxruntime') else 1)" >nul 2>&1
if errorlevel 1 goto setup
goto launch
:setup
echo First setup: Python dependencies and 7.37 GB official shape weights.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%TOOL%\setup.ps1"
if errorlevel 1 goto failed
:launch
"%PYTHON%" -u "%TOOL%\app.py" %*
if errorlevel 1 goto failed
exit /b 0
:failed
echo Setup or generation service failed. Read the error above.
pause
exit /b 1
