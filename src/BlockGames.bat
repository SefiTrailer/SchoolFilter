@echo off
:: ============================================================================
:: SchoolFilter - Lockdown Trigger (Strict Whitelist Only)
:: Enforces PAC whitelist via HKCU Windows Internet Settings
:: Supports optional Cloud PAC URL (central update) or local PAC file fallback
:: Silent execution with immediate WinINet refresh
:: ============================================================================

set "PAC_TARGET=file://C:/Program Files/SchoolFilter/filter.pac"
set "CONFIG_FILE=C:\Program Files\SchoolFilter\config.ini"

:: Check if Cloud PAC URL is configured
if exist "%CONFIG_FILE%" (
    for /f "usebackq tokens=1,* delims==" %%A in ("%CONFIG_FILE%") do (
        if /i "%%A"=="CloudPacUrl" (
            if not "%%B"=="" set "PAC_TARGET=%%B"
        )
    )
)

reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v AutoConfigURL /t REG_SZ /d "%PAC_TARGET%" /f >nul 2>&1

:: Broadcast settings change to active browsers (Edge, Chrome, WinINet)
powershell -NoProfile -NonInteractive -WindowStyle Hidden -Command "$sig = '[DllImport(\"\"wininet.dll\"\", SetLastError=true)] public static extern bool InternetSetOption(IntPtr h, int o, IntPtr b, int l);'; Add-Type -MemberDefinition $sig -Name W -Namespace U; [U.W]::InternetSetOption([IntPtr]::Zero, 39, [IntPtr]::Zero, 0); [U.W]::InternetSetOption([IntPtr]::Zero, 37, [IntPtr]::Zero, 0)" >nul 2>&1

exit /b 0
