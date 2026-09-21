@echo off
setlocal
cd /d "%~dp0"

echo Building MTG Battlegrounds - OG Xbox Bonus Content Converter for Windows v1.0.0...
where dotnet >nul 2>nul
if errorlevel 1 (
    echo.
    echo ERROR: .NET 8 SDK was not found.
    echo Install the .NET 8 SDK, then run this BAT again.
    pause
    exit /b 1
)

if exist "%~dp0Release" rmdir /s /q "%~dp0Release"

dotnet restore
if errorlevel 1 goto :fail

dotnet publish -c Release -r win-x64 -o "%~dp0Release"
if errorlevel 1 goto :fail

set "EXE=%~dp0Release\MtGBgXboxBonusContentConverter.exe"
if not exist "%EXE%" (
    echo.
    echo ERROR: Publish succeeded but the expected EXE was not created.
    goto :fail
)

for /f %%A in ('dir /b /a-d "%~dp0Release" ^| find /c /v ""') do set FILECOUNT=%%A
for /f %%A in ('dir /b /ad "%~dp0Release" ^| find /c /v ""') do set DIRCOUNT=%%A

if not "%FILECOUNT%"=="1" (
    echo.
    echo ERROR: Release contains %FILECOUNT% files instead of exactly one.
    dir /b "%~dp0Release"
    goto :fail
)
if not "%DIRCOUNT%"=="0" (
    echo.
    echo ERROR: Release contains subdirectories; expected a single EXE only.
    dir /b "%~dp0Release"
    goto :fail
)

echo.
echo V1.0.0 SINGLE-EXE BUILD COMPLETE:
echo %EXE%
for %%F in ("%EXE%") do echo Size: %%~zF bytes
echo SHA-256:
powershell -NoProfile -Command "(Get-FileHash -LiteralPath $env:EXE -Algorithm SHA256).Hash"
pause
exit /b 0

:fail
echo.
echo BUILD FAILED.
pause
exit /b 1
