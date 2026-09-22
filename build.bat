@echo off
setlocal
cd /d "%~dp0"

echo ========================================
echo TBH Trainer v1.4.0 - complete build
echo ========================================
echo.

pushd TBHHook
call build.bat
if errorlevel 1 (
    popd
    echo.
    echo HOOK BUILD FAILED
    exit /b 1
)
popd

pushd TBH_Trainer
call build.bat
if errorlevel 1 (
    popd
    echo.
    echo TRAINER BUILD FAILED
    exit /b 1
)
popd

echo.
echo Cleaning up...
REM Keep TBHHook\TBHHook.dll because the trainer project copies it on publish.
if exist TBHHook\dllmain.obj del /Q TBHHook\dllmain.obj
if not exist TrainerBuild mkdir TrainerBuild
copy /Y "TBHHook\TBHHook.dll" "TrainerBuild\TBHHook.dll" >nul
copy /Y "publish\TBH Trainer.exe" "TrainerBuild\TBH Trainer.exe" >nul
copy /Y "publish\TBHHook.dll" "TrainerBuild\TBHHook.dll" >nul 2>nul

echo.
echo ALL BUILD OK
echo Output: publish\ and TrainerBuild\
echo Launch: TrainerBuild\TBH Trainer.exe
endlocal
