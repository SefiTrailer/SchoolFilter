$p = [System.Net.WebRequest]::GetSystemWebProxy()
Write-Host "SystemWebProxy for one-class.co.il:" $p.GetProxy([Uri]'https://one-class.co.il') "Bypassed:" $p.IsBypassed([Uri]'https://one-class.co.il')
Write-Host "SystemWebProxy for roblox.com:     " $p.GetProxy([Uri]'https://www.roblox.com') "Bypassed:" $p.IsBypassed([Uri]'https://www.roblox.com')

$wc = New-Object System.Net.WebClient
$wc.Proxy = $p

try {
    $null = $wc.DownloadString('https://one-class.co.il')
    Write-Host '[PASS] one-class.co.il ALLOWED' -ForegroundColor Green
} catch {
    Write-Host '[FAIL] one-class.co.il BLOCKED:' $_.Exception.Message -ForegroundColor Red
}

try {
    $null = $wc.DownloadString('https://www.roblox.com')
    Write-Host '[FAIL] roblox.com WAS NOT BLOCKED!' -ForegroundColor Red
} catch {
    Write-Host '[PASS] roblox.com BLOCKED:' $_.Exception.Message -ForegroundColor Green
}

try {
    $null = $wc.DownloadString('https://poki.com')
    Write-Host '[FAIL] poki.com WAS NOT BLOCKED!' -ForegroundColor Red
} catch {
    Write-Host '[PASS] poki.com BLOCKED:' $_.Exception.Message -ForegroundColor Green
}
