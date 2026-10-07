@echo off
:: ============================================================================
:: SchoolFilter - Lockdown Trigger (Strict Whitelist Only)
:: Enforces PAC whitelist via WinINet DefaultConnectionSettings + Active Sinkhole
:: ============================================================================

"%~dp0SchoolFilterCtl.exe" block
exit /b 0
