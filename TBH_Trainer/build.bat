@echo off
setlocal
cd /d "%~dp0"

set "DOTNET=dotnet"
where dotnet >nul 2>&1
if errorlevel 1 (
    if exist "%ProgramFiles%\dotnet\dotnet.exe" (
        set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
    ) else (
        echo ERROR: .NET SDK not found.
        echo Install .NET 10 SDK from https://dotnet.microsoft.com/download
        exit /b 1
    )
)

if not exist "..\TBHHook\TBHHook.dll" (
    echo ERROR: ..\TBHHook\TBHHook.dll is missing.
    echo Build the hook first by running ..\TBHHook\build.bat
    exit /b 1
)

"%DOTNET%" publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ..\publish
if errorlevel 1 (
    echo BUILD FAILED
    exit /b 1
)

if not exist "..\TrainerBuild" mkdir "..\TrainerBuild"
copy /Y "..\publish\TBH Trainer.exe" "..\TrainerBuild\TBH Trainer.exe" >nul
copy /Y "..\publish\TBHHook.dll" "..\TrainerBuild\TBHHook.dll" >nul

echo.
echo BUILD OK
echo Run: ..\TrainerBuild\TBH Trainer.exe
endlocal
