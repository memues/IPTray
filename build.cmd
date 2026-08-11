@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo === publishing IPTray (self-contained, win-x64) ===
dotnet publish src\IPTray\IPTray.csproj -c Release -o build\publish --nologo
if errorlevel 1 exit /b 1

set "ISCC="
for %%P in (
    "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
    "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
    "%ProgramFiles%\Inno Setup 6\ISCC.exe"
) do if exist %%P set "ISCC=%%~P"

if not defined ISCC (
    echo.
    echo Inno Setup 6 was not found. Install it with:
    echo     winget install --id JRSoftware.InnoSetup
    exit /b 1
)

echo.
echo === building installer ===
"%ISCC%" /Q installer\IPTray.iss
if errorlevel 1 exit /b 1

echo.
echo Done. Installer is in build\installer
endlocal
