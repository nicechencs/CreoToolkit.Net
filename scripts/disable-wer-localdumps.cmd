@echo off
REM ============================================================================
REM disable-wer-localdumps.cmd
REM
REM Reverse of enable-wer-localdumps.cmd: remove the LocalDumps subkey under
REM   HK*\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\<exe>
REM
REM This script deletes the per-exe subkey under BOTH HKLM and HKCU (whichever
REM exists). The DumpFolder ON DISK is preserved (existing .dmp files are kept;
REM IT or the user can delete them manually).
REM
REM Exit codes:
REM   0  success (at least one hive cleaned, OR no subkey present in either)
REM   2  reg delete failed for an existing subkey
REM
REM Usage:
REM   scripts\disable-wer-localdumps.cmd                 (defaults to xtop.exe)
REM   scripts\disable-wer-localdumps.cmd MyApp.exe
REM ============================================================================
setlocal EnableDelayedExpansion

set "TARGET_EXE=%~1"
if "%TARGET_EXE%"=="" set "TARGET_EXE=xtop.exe"

set "WER_SUBKEY=SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\%TARGET_EXE%"

set "HKLM_PRESENT=0"
set "HKCU_PRESENT=0"
set "HKLM_DELETED=0"
set "HKCU_DELETED=0"

echo === disable-wer-localdumps ===
echo target_exe=%TARGET_EXE%
echo subkey=%WER_SUBKEY%
echo.

REM ---- HKLM cleanup ---------------------------------------------------------
reg query "HKLM\%WER_SUBKEY%" >nul 2>&1
if not errorlevel 1 (
    set "HKLM_PRESENT=1"
    reg delete "HKLM\%WER_SUBKEY%" /f >nul 2>&1
    if errorlevel 1 (
        echo ERROR: HKLM delete failed ^(insufficient privilege?^)
    ) else (
        set "HKLM_DELETED=1"
        echo HKLM subkey deleted.
    )
) else (
    echo HKLM subkey not present, skipping.
)

REM ---- HKCU cleanup ---------------------------------------------------------
reg query "HKCU\%WER_SUBKEY%" >nul 2>&1
if not errorlevel 1 (
    set "HKCU_PRESENT=1"
    reg delete "HKCU\%WER_SUBKEY%" /f >nul 2>&1
    if errorlevel 1 (
        echo ERROR: HKCU delete failed
    ) else (
        set "HKCU_DELETED=1"
        echo HKCU subkey deleted.
    )
) else (
    echo HKCU subkey not present, skipping.
)

REM ---- summary --------------------------------------------------------------
echo.
echo === summary ===
echo hklm_present=%HKLM_PRESENT%
echo hklm_deleted=%HKLM_DELETED%
echo hkcu_present=%HKCU_PRESENT%
echo hkcu_deleted=%HKCU_DELETED%
echo dump_folder_on_disk=preserved
echo note=existing .dmp files under %LOCALAPPDATA%\CreoToolkit\dumps\ are NOT removed.

REM Determine exit code:
REM   - any present hive that failed to delete -> 2
REM   - otherwise 0 (clean or already gone)
if "%HKLM_PRESENT%"=="1" if "%HKLM_DELETED%"=="0" (
    echo result=failure
    endlocal & exit /b 2
)
if "%HKCU_PRESENT%"=="1" if "%HKCU_DELETED%"=="0" (
    echo result=failure
    endlocal & exit /b 2
)

echo result=success
endlocal & exit /b 0
