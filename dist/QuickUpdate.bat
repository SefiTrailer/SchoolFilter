@echo off
title SchoolFilter - Silent Auto-Update
set "DATA_DIR=%ProgramData%\SchoolFilter"
set "PAYLOAD=%DATA_DIR%\remote_update_setup.exe"

if not exist "%DATA_DIR%" mkdir "%DATA_DIR%" >nul 2>&1

:: 1. Immediately unlock internet on the current version while updating
if exist "C:\Program Files\SchoolFilter\SchoolFilterCtl.exe" (
    "C:\Program Files\SchoolFilter\SchoolFilterCtl.exe" allow >nul 2>&1
)

:: 2. Use local SchoolFilter_Setup.exe if sitting in the same folder (e.g. USB / Network Share), otherwise download from Cloud
if exist "%~dp0SchoolFilter_Setup.exe" (
    copy /Y "%~dp0SchoolFilter_Setup.exe" "%PAYLOAD%" >nul 2>&1
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://sefitrailer.github.io/SchoolFilter/dist/SchoolFilter_Setup.exe' -OutFile '%PAYLOAD%' -UseBasicParsing"
)

:: 3. Trigger existing SYSTEM task (runs as NT AUTHORITY\SYSTEM with zero UAC prompts!)
schtasks /Run /TN "SchoolFilterRemoteUpdate" >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo [OK] Update triggered silently via SYSTEM service on %COMPUTERNAME%.
    timeout /t 2 >nul
    exit /b 0
)

:: 4. Fallback direct silent execution
start "" /wait "%PAYLOAD%" /STUDENT /VERYSILENT /SUPPRESSMSGBOXES
echo [OK] Update completed on %COMPUTERNAME%.
