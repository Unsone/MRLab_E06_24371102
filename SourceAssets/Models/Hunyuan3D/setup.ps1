$ErrorActionPreference = 'Stop'
$toolRoot = $PSScriptRoot
$runtimeDir = Join-Path $toolRoot '.runtime'
$pythonExe = Join-Path $runtimeDir 'venv\Scripts\python.exe'
$uvCommand = Get-Command uv -ErrorAction SilentlyContinue
if (-not $uvCommand) {
    $uvCandidate = Join-Path $env:USERPROFILE '.local\bin\uv.exe'
    if (Test-Path -LiteralPath $uvCandidate) { $uvPath = $uvCandidate }
    else { throw 'Install uv (https://docs.astral.sh/uv/) and run this script again.' }
} else { $uvPath = $uvCommand.Source }
if (-not (Test-Path -LiteralPath $pythonExe)) {
    & $uvPath venv --python 3.11 (Join-Path $runtimeDir 'venv')
    if ($LASTEXITCODE -ne 0) { throw 'Python environment creation failed.' }
}
# Scoped package mirror; never changes global pip/uv settings.
& $uvPath pip install --python $pythonExe --index-url 'https://mirrors.aliyun.com/pypi/simple' -r (Join-Path $toolRoot 'requirements-directml.txt')
if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed.' }
& $pythonExe -u (Join-Path $toolRoot 'download_model.py')
if ($LASTEXITCODE -ne 0) { throw 'Weights are not ready. Rerun to resume download.' }
Write-Host 'Setup complete. Run GenerateModel.cmd from the project folder.'
