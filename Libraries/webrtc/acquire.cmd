@echo off

set PATCH_DIR=%~dp0

echo.
echo Downloading the depot_tools...
if exist c:\depot_tools\gclient.bat (
  echo depot_tools already exists, skipping download.
) else (
  curl https://storage.googleapis.com/chrome-infra/depot_tools.zip --output depot_tools.zip
  if errorlevel 1 goto :error

  echo.
  echo Opening the zip file...
  c:
  if not exist c:\depot_tools mkdir c:\depot_tools
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -Path 'depot_tools.zip' -DestinationPath 'C:\depot_tools' -Force"
  if errorlevel 1 goto :error
)

echo.
echo Deleting the depot_tools.zip file
if exist depot_tools.zip del depot_tools.zip

echo.
echo Setting environment variables
set PATH=c:\depot_tools;%PATH%
set DEPOT_TOOLS_WIN_TOOLCHAIN=0
set GYP_MSVS_VERSION=2022

echo.
echo Creating the folder where the code base will be placed...
c:
if not exist c:\webrtc mkdir c:\webrtc

cd c:\webrtc
if errorlevel 1 goto :error

REM Downloading the bits
echo.
echo Telling the gclient tool to initialize your local copy of the repos...
call gclient
if errorlevel 1 goto :error

echo.
echo Requesting the tools to fetch the WebRTC code base...
call fetch --nohooks webrtc
if errorlevel 1 goto :error

echo.
echo Changing to the branch-heads/6312 branch...
cd src
if errorlevel 1 goto :error

call git checkout branch-heads/6312
if errorlevel 1 goto :error

echo.
echo Instructing the tools to bring the bits from all the sub repositories to your dev box...
call gclient sync -D -r branch-heads/6312
if errorlevel 1 goto :error

echo.
echo Adding forked Telegram+UWP upstream
call git remote | findstr /I /R "^upstream$" >nul || call git remote add upstream https://github.com/FrayxRulez/webrtc-uwp.git
call git remote update
call git fetch
call git checkout m123
pushd build
call git apply --3way --ignore-whitespace "%PATCH_DIR%/build/fix.patch" || echo build/fix.patch already applied or skipped.

echo Checking the Architecture type
for /f %%a in ('powershell -NoProfile -ExecutionPolicy Bypass -Command "(Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Architecture)"') do (
    set "cpu_arch=%%a"
    goto :woa-patch
)
:woa-patch
if "%cpu_arch%"=="12" (
    call git apply --3way --ignore-whitespace "%PATCH_DIR%/build/woa_support.patch" || echo build/woa_support.patch already applied or skipped.
)

pushd ..\third_party
call git apply --3way --ignore-whitespace "%PATCH_DIR%/third_party/fix.patch" || echo third_party/fix.patch already applied or skipped.
pushd boringssl\src
call git apply --3way --ignore-whitespace "%PATCH_DIR%/third_party/string.patch" || echo third_party/string.patch already applied or skipped.
pushd ..\..\libyuv
call git apply --3way --ignore-whitespace "%PATCH_DIR%/third_party/libyuv/fix.patch" || echo third_party/libyuv/fix.patch already applied or skipped.
goto :exit

:error
echo Last command failed with erro code: %errorlevel%

:exit
exit /b %errorlevel%
