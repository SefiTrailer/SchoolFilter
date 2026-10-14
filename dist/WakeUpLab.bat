@echo off
chcp 65001 >nul
title SchoolFilter - Wake Up All Lab PCs (NISHMAS01..NISHMAS25)
echo ============================================================
echo   SchoolFilter - Wake Up All Lab PCs (NISHMAS01..NISHMAS25)
echo ============================================================
echo.

for /L %%i in (1,1,25) do (
    if %%i LSS 10 (
        call :WakePC NISHMAS0%%i
    ) else (
        call :WakePC NISHMAS%%i
    )
)

echo.
echo ============================================================
echo   Completed wake-up trigger! Check the dashboard now.
echo ============================================================
pause
exit /b 0

:WakePC
set "PC=%~1"
ping -n 1 -w 200 %PC% >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [-] %PC% is unreachable on LAN (powered off or disconnected)
    goto :eof
)

echo [+] %PC% is reachable - starting SchoolFilter service...
wmic /node:"%PC%" process call create "cmd.exe /c start \"\" \"C:\Program Files\SchoolFilter\SchoolFilterCtl.exe\" sinkhole" >nul 2>&1
schtasks /Run /S %PC% /TN "SchoolFilterSystemAgent" >nul 2>&1
schtasks /Run /S %PC% /TN "SchoolFilterGuard" >nul 2>&1
goto :eof
