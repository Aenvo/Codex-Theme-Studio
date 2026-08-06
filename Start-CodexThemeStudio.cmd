@echo off
setlocal

if not defined windir set "windir=%SystemRoot%"

set "DOTNET=%LOCALAPPDATA%\CodexThemeStudio\devtools\dotnet-8.0.423\dotnet.exe"
set "PROJECT=%~dp0src\CodexThemeStudio.Desktop\CodexThemeStudio.Desktop.csproj"
set "APP=%~dp0src\CodexThemeStudio.Desktop\bin\Release\net8.0-windows\win-x64\CodexThemeStudio.Desktop.exe"

powershell.exe -NoProfile -NonInteractive -Command "if (Get-Process -Name 'CodexThemeManager','CodexThemeStudio.Desktop' -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }"
if not errorlevel 1 (
    echo Another Codex Theme Studio instance is still running.
    echo Close CodexThemeManager.exe or CodexThemeStudio.Desktop.exe, then run this launcher again.
    pause
    exit /b 2
)

if not exist "%DOTNET%" (
    echo Codex Theme Studio development SDK was not found:
    echo "%DOTNET%"
    pause
    exit /b 1
)

"%DOTNET%" build "%PROJECT%" -c Release --no-restore --nologo -v:minimal --disable-build-servers /m:1 /nodeReuse:false /p:UseSharedCompilation=false /p:CodexRuntimeIdentifier=win-x64 /p:SelfContained=false
if errorlevel 1 (
    echo.
    echo Codex Theme Studio build failed.
    pause
    exit /b 1
)

if not exist "%APP%" (
    echo Codex Theme Studio executable was not found:
    echo "%APP%"
    pause
    exit /b 1
)

start "" "%APP%"
exit /b 0
