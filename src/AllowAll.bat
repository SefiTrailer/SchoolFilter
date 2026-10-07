@echo off
:: ============================================================================
:: SchoolFilter - Restore Trigger (Full Internet Access)
:: Removes PAC configuration and restores direct internet connectivity
:: ============================================================================

"%~dp0SchoolFilterCtl.exe" allow
exit /b 0
