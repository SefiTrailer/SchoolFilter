@echo off
title SchoolFilter - Silent Auto-Update to v3.6
set "DATA_DIR=%ProgramData%\SchoolFilter"
set "PAYLOAD=%DATA_DIR%\remote_update_setup.exe"
set "TEMP_PAYLOAD=%TEMP%\SchoolFilter_Setup.exe"

if not exist "%DATA_DIR%" mkdir "%DATA_DIR%" >nul 2>&1

:: 1. Download or copy installer (try ProgramData first for legacy SYSTEM task, fallback to TEMP)
if exist "%~dp0SchoolFilter_Setup.exe" (
    copy /Y "%~dp0SchoolFilter_Setup.exe" "%PAYLOAD%" >nul 2>&1
    copy /Y "%~dp0SchoolFilter_Setup.exe" "%TEMP_PAYLOAD%" >nul 2>&1
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = 3072; $wc = New-Object System.Net.WebClient; $wc.Proxy = $null; try { $wc.DownloadFile('https://sefitrailer.github.io/SchoolFilter/dist/SchoolFilter_Setup.exe', '%PAYLOAD%') } catch {}; $wc.DownloadFile('https://sefitrailer.github.io/SchoolFilter/dist/SchoolFilter_Setup.exe', '%TEMP_PAYLOAD%')"
)

:: 2. If legacy SchoolFilterRemoteUpdate task exists (v3.2/v3.3), trigger it as SYSTEM (zero UAC prompt)
if exist "%PAYLOAD%" (
    schtasks /Run /TN "SchoolFilterRemoteUpdate" >nul 2>&1
    schtasks /Run /TN "SchoolFilterSystemAgent" >nul 2>&1
    if %ERRORLEVEL% EQU 0 (
        echo [OK] Update to v3.6 triggered silently via SYSTEM task on %COMPUTERNAME%.
        timeout /t 2 >nul
        exit /b 0
    )
)

:: 3. Direct silent execution (when run as Admin / Veyon, or elevates via UAC on v3.1)
if exist "%TEMP_PAYLOAD%" (
    start "" /wait "%TEMP_PAYLOAD%" /STUDENT /VERYSILENT /SUPPRESSMSGBOXES
    echo [OK] Update to v3.6 completed on %COMPUTERNAME%.
)
