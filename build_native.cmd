@echo off
REM ============================================================================
REM build_native.cmd - Configure and build one native CMake target.
REM
REM Usage:
REM   build_native.cmd <target> [Debug|Release] [v140|v143]
REM
REM VC140 is the production default because Creo 4 uses the VS2015 ABI.
REM Set CTK_NATIVE_TOOLSET=v143 or pass v143 explicitly for a modern-toolset
REM compatibility build.
REM ============================================================================
setlocal

set "TARGET=%~1"
set "CONFIG=%~2"
set "TOOLSET=%~3"
if "%TARGET%"=="" (
    echo ERROR: missing CMake target.
    exit /b 2
)
if "%CONFIG%"=="" set "CONFIG=Release"
if "%TOOLSET%"=="" set "TOOLSET=%CTK_NATIVE_TOOLSET%"
if "%TOOLSET%"=="" set "TOOLSET=v140"

set "ROOT=%~dp0."
for %%I in ("%ROOT%") do set "ROOT=%%~fI"
set "BUILD_DIR=%ROOT%\cmake"

if /I "%TOOLSET%"=="v140" goto :build_v140
if /I not "%TOOLSET%"=="v143" (
    echo ERROR: unsupported native toolset "%TOOLSET%". Expected v140 or v143.
    exit /b 4
)

if "%CMAKE_EXE%"=="" (
    if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe" (
        set "CMAKE_EXE=C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
    )
)
if "%CMAKE_EXE%"=="" (
    for /f "delims=" %%I in ('where cmake 2^>nul') do (
        set "CMAKE_EXE=%%I"
        goto :found_cmake
    )
)
:found_cmake
if "%CMAKE_EXE%"=="" (
    echo ERROR: cmake.exe not found. Set CMAKE_EXE or install Visual Studio CMake tools.
    exit /b 3
)

echo === CMake configure: %BUILD_DIR% ===
"%CMAKE_EXE%" -S "%ROOT%" -B "%BUILD_DIR%" -G "Visual Studio 17 2022" -A x64
if errorlevel 1 exit /b %errorlevel%

echo === CMake build: target=%TARGET% config=%CONFIG% ===
"%CMAKE_EXE%" --build "%BUILD_DIR%" --config "%CONFIG%" --target "%TARGET%"
set "BUILD_RC=%errorlevel%"
if not "%BUILD_RC%"=="0" exit /b %BUILD_RC%
goto :build_done

:build_v140
echo === VC140 native build: target=%TARGET% config=%CONFIG% ===
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%ROOT%\scripts\build-native-v140.ps1" -Target "%TARGET%" -Configuration "%CONFIG%"
set "BUILD_RC=%errorlevel%"
if not "%BUILD_RC%"=="0" exit /b %BUILD_RC%

:build_done
exit /b 0
