param(
    [string]$AdminUser = "",
    [string]$AdminPass = ""
)

if ([string]::IsNullOrWhiteSpace($AdminUser) -and $u) { $AdminUser = $u }
if ([string]::IsNullOrWhiteSpace($AdminPass) -and $p) { $AdminPass = $p }

$ErrorActionPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[System.Net.WebRequest]::DefaultWebProxy = $null

# 1. Temporarily clear local proxy on legacy v3.1 and reset state.txt to auto
$legacyCtl = "C:\Program Files\SchoolFilter\SchoolFilterCtl.exe"
if (Test-Path $legacyCtl) {
    & $legacyCtl allow | Out-Null
}

$dataDir = "C:\ProgramData\SchoolFilter"
if (!(Test-Path $dataDir)) {
    New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
}
try {
    [IO.File]::WriteAllText("$dataDir\state.txt", "auto")
    [IO.File]::WriteAllText("$dataDir\trigger_update.txt", "1")
} catch {}

# 2. Download v3.4 Setup directly bypassing any system proxy
$wc = New-Object System.Net.WebClient
$wc.Proxy = $null
$urls = @(
    "https://raw.githubusercontent.com/SefiTrailer/SchoolFilter/main/dist/SchoolFilter_Setup.exe",
    "https://sefitrailer.github.io/SchoolFilter/dist/SchoolFilter_Setup.exe"
)

$sharedSetup = "$dataDir\remote_update_setup.exe"
$tempSetup = "$env:TEMP\SchoolFilter_Setup_v34.exe"
$downloaded = $false

foreach ($u in $urls) {
    try {
        $bustUrl = $u + "?t=" + [DateTime]::UtcNow.Ticks
        $wc.DownloadFile($bustUrl, $tempSetup)
        if ((Test-Path $tempSetup) -and ((Get-Item $tempSetup).Length -gt 50000)) {
            $downloaded = $true
            break
        }
    } catch {}
}

if (!$downloaded) { exit 1 }

try {
    Copy-Item -Path $tempSetup -Destination $sharedSetup -Force
} catch {}

$activeSetup = if (Test-Path $sharedSetup) { $sharedSetup } else { $tempSetup }

# 3. Try Elevated SYSTEM / Admin Upgrade into C:\Program Files\SchoolFilter
try {
    schtasks.exe /Run /TN "SchoolFilterRemoteUpdate" | Out-Null
} catch {}

if (![string]::IsNullOrWhiteSpace($AdminUser) -and ![string]::IsNullOrWhiteSpace($AdminPass)) {
    $cleanUser = $AdminUser.Trim()
    if ($cleanUser.Contains("\")) {
        $cleanUser = ($cleanUser -split "\\")[-1]
    }
    $localFullUser = "$env:COMPUTERNAME\$cleanUser"

    # Method A: schtasks via 127.0.0.1 loopback RPC
    try {
        schtasks.exe /Run /S 127.0.0.1 /U $localFullUser /P $AdminPass /TN "SchoolFilterRemoteUpdate" | Out-Null
        schtasks.exe /Run /S 127.0.0.1 /U $cleanUser /P $AdminPass /TN "SchoolFilterRemoteUpdate" | Out-Null
    } catch {}

    # Method B: Schedule.Service COM API
    foreach ($srv in @("127.0.0.1", $null)) {
        try {
            $sched = New-Object -ComObject Schedule.Service
            $sched.Connect($srv, $cleanUser, $env:COMPUTERNAME, $AdminPass)
            $folder = $sched.GetFolder("\")
            $task = $folder.GetTask("SchoolFilterRemoteUpdate")
            $task.Run($null) | Out-Null
        } catch {}
    }

    # Method C: Start-Process with PSCredential (Interactive logon as AdminUser)
    try {
        $secPass = ConvertTo-SecureString $AdminPass -AsPlainText -Force
        $cred = New-Object System.Management.Automation.PSCredential($localFullUser, $secPass)
        $innerCmd = "schtasks.exe /Run /TN 'SchoolFilterRemoteUpdate'; Start-Process -FilePath '$activeSetup' -ArgumentList '/STUDENT /VERYSILENT /SUPPRESSMSGBOXES' -WindowStyle Hidden"
        Start-Process -FilePath "powershell.exe" -Credential $cred -ArgumentList "-NoProfile -WindowStyle Hidden -Command `"$innerCmd`"" -WindowStyle Hidden
    } catch {}
}

Start-Sleep -Seconds 5

# 4. Verify if C:\Program Files\SchoolFilter\SchoolFilterCtl.exe was updated to v3.4 (>= 80 KB)
$pfUpdated = $false
if (Test-Path $legacyCtl) {
    if ((Get-Item $legacyCtl).Length -ge 80000) {
        $pfUpdated = $true
    }
}

if ($pfUpdated) {
    # Ensure user-session watchdog is running the updated v3.4 binary
    Get-Process -Name "SchoolFilterCtl" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 400
    Start-Process -FilePath $legacyCtl -ArgumentList "watchdog" -WindowStyle Hidden
} else {
    # 5. Guaranteed Fallback: Install v3.4 directly into user-accessible directory and take over user session immediately
    $fallbackDir = "$dataDir\App"
    try {
        if (!(Test-Path $fallbackDir)) { New-Item -ItemType Directory -Path $fallbackDir -Force -ErrorAction Stop | Out-Null }
        $testFile = "$fallbackDir\_test.tmp"
        [IO.File]::WriteAllText($testFile, "1")
        Remove-Item $testFile -Force
    } catch {
        $fallbackDir = "$env:LOCALAPPDATA\SchoolFilter"
    }

    Get-Process -Name "SchoolFilterCtl" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 400

    $dirArg = "`"/DIR=$fallbackDir`" /STUDENT /VERYSILENT /SUPPRESSMSGBOXES"
    Start-Process -FilePath $activeSetup -ArgumentList $dirArg -Wait -WindowStyle Hidden

    $newCtl = "$fallbackDir\SchoolFilterCtl.exe"
    if (Test-Path $newCtl) {
        Start-Process -FilePath $newCtl -ArgumentList "watchdog" -WindowStyle Hidden
    }
}
