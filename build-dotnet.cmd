@echo off
REM ============================================================================
REM build-dotnet.cmd
REM
REM Builds the entire CreoToolkit .NET tree via CreoToolkitRefactor.slnx
REM [XML solution format, dotnet 8+]. Directory.Build.props at this root
REM pins Platform=x64 and UTF-8 code page for every project.
REM
REM Stages [any FAIL aborts]:
REM   1. dotnet restore
REM   2. dotnet build -c <CFG>
REM
REM Usage:    build-dotnet.cmd [Configuration]
REM Example:  build-dotnet.cmd
REM           build-dotnet.cmd Debug
REM
REM Notes:
REM   * Default Configuration is Release.
REM   * For full verification incl. tests, use scripts\r5-verify-no-creo.cmd.
REM ============================================================================
setlocal EnableDelayedExpansion

set "ROOT=%~dp0."
for %%I in ("%ROOT%") do set "ROOT=%%~fI"
cd /d "%ROOT%"

set "CFG=%~1"
if "%CFG%"=="" set "CFG=Release"

echo === ROOT: %ROOT% ===
echo === Configuration: %CFG% ===

echo.
echo === [1/2] dotnet restore ===
dotnet restore --verbosity minimal || goto :fail_restore

echo.
echo === [2/2] dotnet build -c %CFG% ===
dotnet build -c %CFG% --no-restore --verbosity minimal || goto :fail_build

echo.
echo ============================================================
echo  dotnet build [%CFG%]: SUCCESS
echo ============================================================
exit /b 0

:fail_restore
echo. & echo *** dotnet restore FAILED *** & exit /b 11
:fail_build
echo. & echo *** dotnet build FAILED *** & exit /b 12
