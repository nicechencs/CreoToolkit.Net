@echo off
REM ============================================================================
REM enable-wer-localdumps.cmd
REM
REM Register WER (Windows Error Reporting) LocalDumps for a target exe so that
REM crashes (unhandled SEH, __fastfail, RaiseFailFastException, stack overflow)
REM produce a minidump under DumpFolder. Designed for CreoToolkit native-host
REM bootstrap diagnostics (track C, see structured-logging-design-v4.1.md Sec 6).
REM
REM HKLM is the only registry hive WER guarantees to honor; HKCU LocalDumps is
REM ignored by some Win10+ builds. This script prefers HKLM (admin) and falls
REM back to HKCU (best-effort, for non-admin smoke testing) only when needed.
REM
REM Registry values written under
REM   HK*\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\<exe>
REM
REM   DumpFolder       REG_EXPAND_SZ  %LOCALAPPDATA%\CreoToolkit\dumps\
REM   DumpType         REG_DWORD      0           (Custom; uses CustomDumpFlags)
REM   CustomDumpFlags  REG_DWORD      0x00001125  DataSegs|HandleData|
REM                                               UnloadedModules|
REM                                               ProcessThreadData|ThreadInfo
REM   DumpCount        REG_DWORD      3           keep last 3, FIFO
REM
REM Exit codes:
REM   0  success (HKLM or HKCU write completed and verified)
REM   1  ACL probe failed and OS-default fallback chosen (not a hard error,
REM      but signals that DumpFolder differs from the requested path)
REM   2  reg write failed for the chosen hive
REM   3  neither HKLM nor HKCU writable
REM
REM Usage:
REM   scripts\enable-wer-localdumps.cmd                 (defaults to xtop.exe)
REM   scripts\enable-wer-localdumps.cmd MyApp.exe
REM ============================================================================
setlocal EnableDelayedExpansion

set "TARGET_EXE=%~1"
if "%TARGET_EXE%"=="" set "TARGET_EXE=xtop.exe"

set "DUMP_DIR_DEFAULT=%LOCALAPPDATA%\CreoToolkit\dumps"
set "DUMP_DIR=%DUMP_DIR_DEFAULT%"
set "ACL_OK=1"
set "HIVE="

set "WER_SUBKEY=SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\%TARGET_EXE%"

echo === enable-wer-localdumps ===
echo target_exe=%TARGET_EXE%
echo dump_dir_requested=%DUMP_DIR_DEFAULT%
echo.

REM ---- Step 1: admin probe ---------------------------------------------------
net session >nul 2>&1
if errorlevel 1 (
    set "IS_ADMIN=0"
) else (
    set "IS_ADMIN=1"
)
echo is_admin=%IS_ADMIN%

REM ---- Step 2: create DumpFolder --------------------------------------------
if not exist "%DUMP_DIR%" (
    mkdir "%DUMP_DIR%" 2>nul
    if errorlevel 1 (
        echo WARN: mkdir failed for "%DUMP_DIR%"
        set "ACL_OK=0"
    )
)

REM ---- Step 3: ACL write probe ----------------------------------------------
if "%ACL_OK%"=="1" (
    set "PROBE=%DUMP_DIR%\.write_probe.tmp"
    >"!PROBE!" echo .
    if not exist "!PROBE!" (
        echo WARN: ACL write probe failed under "%DUMP_DIR%"
        set "ACL_OK=0"
    ) else (
        del /q "!PROBE!" 2>nul
    )
)

if "%ACL_OK%"=="0" (
    echo WARN: falling back to OS default crash folder ^(no DumpFolder value will be written^)
    set "DUMP_DIR=%LOCALAPPDATA%\CrashDumps"
)

echo dump_dir_effective=%DUMP_DIR%
echo acl_ok=%ACL_OK%
echo.

REM ---- Step 4: pick hive and write registry values --------------------------
if "%IS_ADMIN%"=="1" (
    set "HIVE=HKLM"
    call :write_hive HKLM
    if errorlevel 1 (
        echo WARN: HKLM write failed, retrying under HKCU as best-effort
        set "HIVE=HKCU"
        call :write_hive HKCU
        if errorlevel 1 (
            echo ERROR: neither HKLM nor HKCU writable
            endlocal & exit /b 3
        )
        echo WARN: HKCU LocalDumps is best-effort; not officially honored on all Windows versions
    )
) else (
    set "HIVE=HKCU"
    echo INFO: not admin, writing HKCU ^(best-effort, not officially supported on all Windows versions^)
    call :write_hive HKCU
    if errorlevel 1 (
        echo ERROR: HKCU write failed
        endlocal & exit /b 2
    )
)

REM ---- Step 5: verify by reg query ------------------------------------------
echo.
echo === verify ===
reg query "%HIVE%\%WER_SUBKEY%" >nul 2>&1
if errorlevel 1 (
    echo ERROR: reg query for %HIVE%\%WER_SUBKEY% failed after write
    endlocal & exit /b 2
)
reg query "%HIVE%\%WER_SUBKEY%"

REM ---- Step 6: summary -------------------------------------------------------
echo.
echo === summary ===
echo hive=%HIVE%
echo subkey=%WER_SUBKEY%
echo dump_folder=%DUMP_DIR%
echo dump_type=0
echo custom_dump_flags=0x00001125
echo dump_count=3
echo target_exe=%TARGET_EXE%
echo acl_probe=%ACL_OK%
if "%ACL_OK%"=="0" (
    echo result=success_with_acl_fallback
    endlocal & exit /b 1
) else (
    echo result=success
    endlocal & exit /b 0
)

REM Guard: prevent fall-through into :write_hive subroutine in case the above
REM if/else somehow exits without hitting an exit /b (defensive only).
goto :eof

REM ---- subroutine: write values into the given hive --------------------------
:write_hive
set "_HIVE=%~1"
if "%ACL_OK%"=="1" (
    reg add "%_HIVE%\%WER_SUBKEY%" /v DumpFolder /t REG_EXPAND_SZ /d "%DUMP_DIR%" /f >nul 2>&1
    if errorlevel 1 exit /b 1
)
reg add "%_HIVE%\%WER_SUBKEY%" /v DumpType /t REG_DWORD /d 0 /f >nul 2>&1
if errorlevel 1 exit /b 1
reg add "%_HIVE%\%WER_SUBKEY%" /v CustomDumpFlags /t REG_DWORD /d 0x00001125 /f >nul 2>&1
if errorlevel 1 exit /b 1
reg add "%_HIVE%\%WER_SUBKEY%" /v DumpCount /t REG_DWORD /d 3 /f >nul 2>&1
if errorlevel 1 exit /b 1
exit /b 0
