@echo off
setlocal
set "CACHE=%~dp0..\.installer-cache"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSDIR=%%i"
if not defined VSDIR exit /b 1
call "%VSDIR%\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64
if errorlevel 1 exit /b 1
pushd "%CACHE%\make-4.4.1"
call build_w32.bat --without-guile
if errorlevel 1 exit /b 1
popd
set "PATH=%CACHE%\make-4.4.1\WinRel;%PATH%"
set "MSYS2_ARG_CONV_EXCL=*"
set "VSLANG=1033"
"C:\Program Files\Git\bin\bash.exe" "%~dp0BuildFfmpeg.sh"
exit /b %errorlevel%
