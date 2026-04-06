@echo off
setlocal EnableDelayedExpansion

echo.
echo Setting environment variables
set PATH=c:\depot_tools;%PATH%
set DEPOT_TOOLS_WIN_TOOLCHAIN=0
set GYP_MSVS_VERSION=2022
set GYP_MSVS_OVERRIDE_PATH=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools
set SDK_VER=10.0.26100.0

REM Clear stale inherited SDK paths from the parent shell.
set INCLUDE=
set LIB=
set LIBPATH=
set WindowsSDKVersion=
cd c:\webrtc\src
if errorlevel 1 goto :error

echo.
echo Opening the developer command prompt

echo Checking the Architecture type
for /f %%a in ('powershell -NoProfile -ExecutionPolicy Bypass -Command "(Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Architecture)"') do (
    set "cpu_arch=%%a"
)

if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat" (
    set "VSDEVCMD=C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"
) else if exist "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat" (
    set "VSDEVCMD=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat"
) else (
    echo Could not find VsDevCmd.bat in Community or BuildTools install.
    goto :error
)

if "%cpu_arch%"=="12" (
    call "%VSDEVCMD%" -arch=arm64 -winsdk=%SDK_VER%
) else (
    call "%VSDEVCMD%" -arch=amd64 -winsdk=%SDK_VER%
)
if errorlevel 1 goto :error

set WindowsSDKVersion=%SDK_VER%\

if "%~1"=="" (set ARCH_LIST=x64) else (set ARCH_LIST=%~1)
if "%~2"=="" (set CFG_LIST=release) else (set CFG_LIST=%~2)

for %%a in (!ARCH_LIST!) do (
    set "target_cpu=%%a"
    if /I "%%a"=="win32" set "target_cpu=x86"

    for %%c in (!CFG_LIST!) do (
        if /I %%c==release (set is_debug=false) else (set is_debug=true)

        echo.
        echo Preparing to build the drop for UWP %%a is_debug=!is_debug!
        call gn gen --ide=vs2022 out\msvc\uwp\%%c\!target_cpu! --filters=//:webrtc "--args=is_debug=!is_debug! use_lld=false is_clang=false rtc_include_tests=false rtc_build_tools=false rtc_win_video_capture_winrt=true target_os=\"winuwp\" rtc_build_examples=false rtc_win_use_mf_h264=true rtc_enable_protobuf=false rtc_disable_metrics=true rtc_include_dav1d_in_internal_decoder_factory=false treat_warnings_as_errors=false use_custom_libcxx=false fatal_linker_warnings=false win_sdk_version=\"%SDK_VER%\" target_cpu=\"!target_cpu!\""
        if errorlevel 1 goto :error

        REM Building for UWP target
        echo.
        echo Building the patched WebRTC for UWP %%a is_debug=!is_debug!
        call ninja -C out\msvc\uwp\%%c\!target_cpu!
        if errorlevel 1 goto :error
    )
)

goto :exit

:error
echo Last command failed with error code: %errorlevel%

:exit
exit /b %errorlevel%
