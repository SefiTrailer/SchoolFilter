@echo off
:: ============================================================================
:: SchoolFilter - Restore Trigger (Full Internet Access)
:: Removes PAC configuration and restores direct internet connectivity
:: Silent execution with immediate WinINet refresh
:: ============================================================================

reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v AutoConfigURL /f >nul 2>&1

:: Broadcast settings change to active browsers (Edge, Chrome, WinINet)
powershell -NoProfile -NonInteractive -WindowStyle Hidden -Command "$sig = '[DllImport(\"\"wininet.dll\"\", SetLastError=true)] public static extern bool InternetSetOption(IntPtr h, int o, IntPtr b, int l);'; Add-Type -MemberDefinition $sig -Name W -Namespace U; [U.W]::InternetSetOption([IntPtr]::Zero, 39, [IntPtr]::Zero, 0); [U.W]::InternetSetOption([IntPtr]::Zero, 37, [IntPtr]::Zero, 0)" >nul 2>&1

exit /b 0
