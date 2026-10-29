@echo off
setlocal
set "BEAKER_FILE=%~dp0SourceAssets\Models\Beaker_Empty_10cm\Beaker_Empty_10cm.blend"
if not exist "%BEAKER_FILE%" (
  echo Repaired Blender file was not found.
  pause
  exit /b 1
)
if exist "D:\Blender 4.2\blender.exe" (
  start "" "D:\Blender 4.2\blender.exe" "%BEAKER_FILE%"
) else (
  start "" "%BEAKER_FILE%"
)
