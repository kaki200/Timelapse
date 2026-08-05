@echo off
echo Building Timelapse-ViewOnly wrapper...

REM Accept configuration as parameter (Debug or Release), default to Release
if "%~1"=="" (
    set CONFIG=Release
) else (
    set CONFIG=%~1
)

REM Set output directory to main Timelapse build folder
set OUTPUT_DIR=..\Timelapse\bin\%CONFIG%\net10.0-windows\win-x64

REM Locate a Visual Studio edition containing the C++ toolchain, then initialize it
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo Visual Studio Installer's vswhere.exe was not found
    exit /b 1
)
set "VS_INSTALL_PATH="
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VS_INSTALL_PATH=%%i"
if not defined VS_INSTALL_PATH (
    echo Visual Studio C++ build tools were not found
    exit /b 1
)
call "%VS_INSTALL_PATH%\Common7\Tools\VsDevCmd.bat"
if %ERRORLEVEL% NEQ 0 exit /b %ERRORLEVEL%

REM Compile resource file
echo Compiling resource file...
rc TimelapseViewOnly.rc
if %ERRORLEVEL% NEQ 0 (
    echo Failed to compile resource file
    exit /b 1
)

REM Compile the wrapper executable
echo Compiling Timelapse-ViewOnly.exe...
cl.exe /Fe:"%OUTPUT_DIR%\Timelapse-ViewOnly.exe" TimelapseViewOnly.cpp TimelapseViewOnly.res /link /SUBSYSTEM:WINDOWS
if %ERRORLEVEL% NEQ 0 (
    echo Failed to compile executable
    exit /b 1
)

REM Clean up temporary files
del *.obj *.res >NUL 2>&1

echo Timelapse-ViewOnly.exe built successfully!
echo Configuration: %CONFIG%
echo Output: %OUTPUT_DIR%\Timelapse-ViewOnly.exe
