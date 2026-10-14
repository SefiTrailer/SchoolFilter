@echo off
chcp 65001 >nul
title SchoolFilter - Mass Update Yeshiva Lab (NISHMAS01 - NISHMAS25)
echo ============================================================
echo   SchoolFilter - Automatic LAN Update (NISHMAS01..NISHMAS25)
echo ============================================================
echo.

set "LOCAL_SETUP=%~dp0SchoolFilter_Setup.exe"
if not exist "%LOCAL_SETUP%" (
    set "LOCAL_SETUP=%TEMP%\SchoolFilter_Setup.exe"
    echo Downloading latest SchoolFilter_Setup.exe from Cloud...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = 3072; $wc = New-Object System.Net.WebClient; $wc.Proxy = $null; $wc.DownloadFile('https://sefitrailer.github.io/SchoolFilter/dist/SchoolFilter_Setup.exe', '%TEMP%\SchoolFilter_Setup.exe')"
)

:: Update local machine first
echo [Local] Updating this computer (%COMPUTERNAME%)...
if not exist "%ProgramData%\SchoolFilter" mkdir "%ProgramData%\SchoolFilter" >nul 2>&1
copy /Y "%LOCAL_SETUP%" "%ProgramData%\SchoolFilter\remote_update_setup.exe" >nul 2>&1
schtasks /Run /TN "SchoolFilterRemoteUpdate" >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    start "" /wait "%ProgramData%\SchoolFilter\remote_update_setup.exe" /STUDENT /VERYSILENT /SUPPRESSMSGBOXES
)

:: Loop through NISHMAS01 to NISHMAS25 on the LAN
for /L %%i in (1,1,25) do (
    if %%i LSS 10 (
        call :UpdatePC NISHMAS0%%i
    ) else (
        call :UpdatePC NISHMAS%%i
    )
)

echo.
echo ============================================================
echo   Finished scanning NISHMAS01 - NISHMAS25!
echo ============================================================
pause
exit /b 0

:UpdatePC
set "PC=%~1"
if /I "%PC%"=="%COMPUTERNAME%" goto :eof
ping -n 1 -w 250 %PC% >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [Offline] %PC% is not reachable.
    goto :eof
)
echo [Online]  Pushing update to %PC%...
if exist "\\%PC%\C$\ProgramData\SchoolFilter" (
    copy /Y "%LOCAL_SETUP%" "\\%PC%\C$\ProgramData\SchoolFilter\remote_update_setup.exe" >nul 2>&1
    wmic /node:"%PC%" process call create "cmd.exe /c start \"\" \"C:\ProgramData\SchoolFilter\remote_update_setup.exe\" /STUDENT /VERYSILENT /SUPPRESSMSGBOXES" >nul 2>&1
    if %ERRORLEVEL% EQU 0 (
        echo   -^> [SUCCESS] Updated %PC% via SMB + WMI execution!
        goto :eof
    )
)
wmic /node:"%PC%" process call create "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command \"[Net.ServicePointManager]::SecurityProtocol=3072; $wc=New-Object System.Net.WebClient; $wc.Proxy=$null; $wc.DownloadFile('https://sefitrailer.github.io/SchoolFilter/dist/SchoolFilter_Setup.exe','C:\ProgramData\SchoolFilter\remote_update_setup.exe'); Start-Process 'C:\ProgramData\SchoolFilter\remote_update_setup.exe' -ArgumentList '/STUDENT /VERYSILENT /SUPPRESSMSGBOXES' -Wait\"" >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo   -^> [SUCCESS] Triggered WMI silent download ^& update on %PC%!
) else (
    echo   -^> [INFO] Could not push remotely to %PC% - run QuickUpdate.bat or Veyon command on %PC%.
)
goto :eof
