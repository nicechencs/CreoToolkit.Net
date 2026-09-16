@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "OUT_DIR=%SCRIPT_DIR%out"
if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

rem Standalone no-Creo seam: avoids changing the repository CMake target graph.
cl /nologo /utf-8 /std:c++14 /EHsc /MD /DCTK_APP_TEST_NO_CREO /c ^
  /I"%SCRIPT_DIR%..\..\src\CreoToolkit.NativeAbi\include" ^
  /I"%SCRIPT_DIR%..\..\src\CreoToolkit.NativeAbi\src" ^
  "%SCRIPT_DIR%..\..\src\CreoToolkit.NativeAbi\src\ctk_app.cpp" ^
  /Fo"%OUT_DIR%\ctk_app.obj"
if errorlevel 1 exit /b %errorlevel%
cl /nologo /utf-8 /std:c++14 /EHsc /MD /DCTK_APP_TEST_NO_CREO /c ^
  /I"%SCRIPT_DIR%..\..\src\CreoToolkit.NativeAbi\include" ^
  /I"%SCRIPT_DIR%..\..\src\CreoToolkit.NativeAbi\src" ^
  "%SCRIPT_DIR%ctk_app_lifetime_test.cpp" ^
  /Fo"%OUT_DIR%\ctk_app_lifetime_test.obj"
if errorlevel 1 exit /b %errorlevel%
link /nologo /out:"%OUT_DIR%\ctk_app_lifetime_test.exe" ^
  "%OUT_DIR%\ctk_app.obj" "%OUT_DIR%\ctk_app_lifetime_test.obj"
if errorlevel 1 exit /b %errorlevel%
"%OUT_DIR%\ctk_app_lifetime_test.exe"
