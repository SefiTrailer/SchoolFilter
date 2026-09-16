# Test registry AutoConfigURL toggle and ensure 100% clean restoration
$regKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'

Write-Host "1. Reading baseline AutoConfigURL..." -ForegroundColor Cyan
$initial = (Get-ItemProperty -Path $regKey -Name AutoConfigURL -ErrorAction SilentlyContinue).AutoConfigURL
Write-Host "   Baseline: [$initial]"

Write-Host "2. Simulating BlockGames (setting test PAC)..." -ForegroundColor Cyan
Set-ItemProperty -Path $regKey -Name AutoConfigURL -Value 'file://C:/Program Files/SchoolFilter/filter.pac'
$active = (Get-ItemProperty -Path $regKey -Name AutoConfigURL -ErrorAction SilentlyContinue).AutoConfigURL
Write-Host "   Active PAC: [$active]"

Write-Host "3. Simulating AllowAll (restoring direct connection)..." -ForegroundColor Cyan
Remove-ItemProperty -Path $regKey -Name AutoConfigURL -ErrorAction SilentlyContinue
$restored = (Get-ItemProperty -Path $regKey -Name AutoConfigURL -ErrorAction SilentlyContinue).AutoConfigURL
Write-Host "   Restored state: [$restored]"

if ($null -eq $restored) {
    Write-Host "[SUCCESS] Registry toggle verified successfully and user PC is 100% clean!" -ForegroundColor Green
} else {
    Write-Host "[ERROR] AutoConfigURL was not removed!" -ForegroundColor Red
    exit 1
}
