@echo off
REM Build TBHHook.dll (x64) with MinGW-w64 g++ (preferred) or MSVC cl.
setlocal
cd /d "%~dp0"

where g++ >nul 2>&1
if not errorlevel 1 goto :mingw

echo MinGW g++ not found in PATH, trying MSVC...
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo ERROR: neither g++ nor Visual Studio found
    exit /b 1
)
for /f "usebackq delims=" %%i in (`
    "%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find VC\Auxiliary\Build\vcvars64.bat
`) do set "VCVARS=%%i"
if not defined VCVARS (
    echo ERROR: vcvars64.bat not found
    exit /b 1
)
call "%VCVARS%"
cl /nologo /LD /O2 /EHsc /MT dllmain.cpp /Fe:TBHHook.dll /link kernel32.lib user32.lib
if errorlevel 1 (
    echo BUILD FAILED
    exit /b 1
)
goto :done

:mingw
echo Building with MinGW-w64...
g++ -shared -O2 -std=c++17 -static -s -o TBHHook.dll dllmain.cpp -lkernel32 -luser32
if errorlevel 1 (
    echo BUILD FAILED
    exit /b 1
)

:done
if not exist "..\TrainerBuild" mkdir "..\TrainerBuild"
copy /Y TBHHook.dll "..\TrainerBuild\TBHHook.dll" >nul
echo BUILD OK - TBHHook.dll copied to TrainerBuild
endlocal
