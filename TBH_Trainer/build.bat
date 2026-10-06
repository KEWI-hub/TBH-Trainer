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
rem A running trainer locks its own exe, so this copy fails while one is open. Reporting
rem BUILD OK anyway leaves the previous build in TrainerBuild and it gets tested instead.
if errorlevel 1 (
    echo COPY FAILED - "..\TrainerBuild\TBH Trainer.exe" is locked. Close the trainer and build again.
    exit /b 1
)
rem Take the hook straight from where TBHHookuild.bat puts it. It used to be copied out of
rem ..\publish, but the SDK stopped emitting it there, which left publish without a hook at all
rem and would have shipped a zip missing TBHHook.dll.
copy /Y "..\TBHHook\TBHHook.dll" "..\publish\TBHHook.dll" >nul
if errorlevel 1 (
    echo COPY FAILED - cannot write "..\publish\TBHHook.dll".
    exit /b 1
)
rem The game locks TBHHook.dll after injection. Most trainer builds do not change the hook,
rem so only copy when it actually differs - otherwise an open game fails the build for nothing.
fc /b "..\TBHHook\TBHHook.dll" "..\TrainerBuild\TBHHook.dll" >nul 2>&1
if errorlevel 1 (
    copy /Y "..\TBHHook\TBHHook.dll" "..\TrainerBuild\TBHHook.dll" >nul
    if errorlevel 1 (
        echo COPY FAILED - "..\TrainerBuild\TBHHook.dll" is locked and out of date. Close the game and build again.
        exit /b 1
    )
)

echo.
echo BUILD OK
echo Run: ..\TrainerBuild\TBH Trainer.exe
endlocal
