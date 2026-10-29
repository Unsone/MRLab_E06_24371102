@echo off
setlocal
set "PC_EXE=%~dp0Builds\PC\MRLab_E06_24371102.exe"
if not exist "%PC_EXE%" (
  echo PC build not found. Please obtain the complete Builds\PC folder.
  pause
  exit /b 1
)
start "" /D "%~dp0Builds\PC" "%PC_EXE%"
