param(
    [string]$AdminUser = "",
    [string]$AdminPass = ""
)

if ([string]::IsNullOrWhiteSpace($AdminUser) -and $u) { $AdminUser = $u }
if ([string]::IsNullOrWhiteSpace($AdminPass) -and $p) { $AdminPass = $p }

$ErrorActionPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[System.Net.WebRequest]::DefaultWebProxy = $null

$scriptStartUtc = [DateTime]::UtcNow.AddSeconds(-10)

# 1. Temporarily clear current user's WinINet AutoConfigURL so download cannot be blocked by a stopped sinkhole
try {
    $inetKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings"
    Remove-ItemProperty -Path $inetKey -Name "AutoConfigURL" -ErrorAction SilentlyContinue
} catch {}

# Stop any running SchoolFilterCtl (watchdog + sinkhole) so binaries are unlocked for upgrade
for ($k = 0; $k -lt 4; $k++) {
    try {
        $tc = New-Object System.Net.Sockets.TcpClient("127.0.0.1", 9999)
        $st = $tc.GetStream()
        $bytes = [System.Text.Encoding]::ASCII.GetBytes("STOP_SINKHOLE`r`n`r`n")
        $st.Write($bytes, 0, $bytes.Length)
        $st.Flush()
        $tc.Close()
    } catch {}
    Get-Process -Name "SchoolFilterCtl" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    taskkill.exe /F /IM SchoolFilterCtl.exe 2>&1 | Out-Null
    Start-Sleep -Milliseconds 250
}

$dataDir = "C:\ProgramData\SchoolFilter"
if (!(Test-Path $dataDir)) {
    New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
}
try {
    [IO.File]::WriteAllText("$dataDir\state.txt", "auto")
    [IO.File]::WriteAllText("$dataDir\trigger_update.txt", "1")
} catch {}

# 2. Download latest v3.4 Setup directly bypassing any system proxy
$wc = New-Object System.Net.WebClient
$wc.Proxy = $null
$urls = @(
    "https://raw.githubusercontent.com/SefiTrailer/SchoolFilter/main/dist/SchoolFilter_Setup.exe",
    "https://sefitrailer.github.io/SchoolFilter/dist/SchoolFilter_Setup.exe"
)

$publicSetup = "C:\Users\Public\SchoolFilter_Setup_v34.exe"
$tempSetup = "$env:TEMP\SchoolFilter_Setup_v34.exe"
$sharedSetup = "$dataDir\remote_update_setup.exe"
$activeSetup = $null

foreach ($u in $urls) {
    foreach ($dest in @($publicSetup, $tempSetup)) {
        try {
            if (Test-Path $dest) { Remove-Item $dest -Force -ErrorAction SilentlyContinue }
            $bustUrl = $u + "?t=" + [DateTime]::UtcNow.Ticks
            $wc.DownloadFile($bustUrl, $dest)
            if ((Test-Path $dest) -and ((Get-Item $dest).Length -gt 50000)) {
                $activeSetup = $dest
                break
            }
        } catch {}
    }
    if ($activeSetup) { break }
}

if (!$activeSetup) { exit 1 }

try {
    Copy-Item -Path $activeSetup -Destination $sharedSetup -Force -ErrorAction SilentlyContinue
} catch {}

# 3. Try Elevated SYSTEM / Admin Upgrade into C:\Program Files\SchoolFilter
$legacyCtl = "C:\Program Files\SchoolFilter\SchoolFilterCtl.exe"
try {
    schtasks.exe /Run /TN "SchoolFilterRemoteUpdate" 2>&1 | Out-Null
} catch {}

if (![string]::IsNullOrWhiteSpace($AdminUser) -and ![string]::IsNullOrWhiteSpace($AdminPass)) {
    $cleanUser = $AdminUser.Trim()
    if ($cleanUser.Contains("\")) {
        $cleanUser = ($cleanUser -split "\\")[-1]
    }
    $localFullUser = "$env:COMPUTERNAME\$cleanUser"

    try {
        schtasks.exe /Run /S 127.0.0.1 /U $localFullUser /P $AdminPass /TN "SchoolFilterRemoteUpdate" 2>&1 | Out-Null
        schtasks.exe /Run /S 127.0.0.1 /U $cleanUser /P $AdminPass /TN "SchoolFilterRemoteUpdate" 2>&1 | Out-Null
    } catch {}

    try {
        $secPass = ConvertTo-SecureString $AdminPass -AsPlainText -Force
        $cred = New-Object System.Management.Automation.PSCredential($localFullUser, $secPass)
        $innerCmd = "Start-Process -FilePath '$activeSetup' -ArgumentList '/STUDENT /VERYSILENT /SUPPRESSMSGBOXES' -WindowStyle Hidden"
        Start-Process -FilePath "powershell.exe" -Credential $cred -ArgumentList "-NoProfile -WindowStyle Hidden -Command `"$innerCmd`"" -WindowStyle Hidden
    } catch {}
}

Start-Sleep -Seconds 3

# 4. Check if C:\Program Files\SchoolFilter\SchoolFilterCtl.exe was freshly updated during this run
$pfUpdated = $false
if (Test-Path $legacyCtl) {
    $item = Get-Item $legacyCtl
    if ($item.Length -ge 80000 -and $item.LastWriteTimeUtc -ge $scriptStartUtc) {
        $pfUpdated = $true
    }
}

# Kill any old SchoolFilterCtl processes before starting the newly installed one
for ($k = 0; $k -lt 3; $k++) {
    Get-Process -Name "SchoolFilterCtl" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    taskkill.exe /F /IM SchoolFilterCtl.exe 2>&1 | Out-Null
    Start-Sleep -Milliseconds 200
}

$launchedCtl = $null

if ($pfUpdated) {
    $launchedCtl = $legacyCtl
    Start-Process -FilePath $legacyCtl -ArgumentList "watchdog" -WindowStyle Hidden
} else {
    # 5. Guaranteed Fallback: Install latest v3.4 directly into user-writable directory and take over user session immediately
    $fallbackDir = "$dataDir\App"
    try {
        if (!(Test-Path $fallbackDir)) { New-Item -ItemType Directory -Path $fallbackDir -Force -ErrorAction Stop | Out-Null }
        $testFile = "$fallbackDir\_test.tmp"
        [IO.File]::WriteAllText($testFile, "1")
        Remove-Item $testFile -Force -ErrorAction Stop
        if (Test-Path "$fallbackDir\SchoolFilterCtl.exe") {
            Remove-Item "$fallbackDir\SchoolFilterCtl.exe" -Force -ErrorAction Stop
        }
    } catch {
        $fallbackDir = "$env:LOCALAPPDATA\SchoolFilter"
        if (!(Test-Path $fallbackDir)) { New-Item -ItemType Directory -Path $fallbackDir -Force | Out-Null }
        if (Test-Path "$fallbackDir\SchoolFilterCtl.exe") {
            Remove-Item "$fallbackDir\SchoolFilterCtl.exe" -Force -ErrorAction SilentlyContinue
        }
    }

    $dirArg = "`"/DIR=$fallbackDir`" /STUDENT /VERYSILENT /SUPPRESSMSGBOXES"
    Start-Process -FilePath $activeSetup -ArgumentList $dirArg -Wait -WindowStyle Hidden

    $newCtl = "$fallbackDir\SchoolFilterCtl.exe"
    if (Test-Path $newCtl) {
        $launchedCtl = $newCtl
        try {
            $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
            Set-ItemProperty -Path $runKey -Name "SchoolFilter" -Value "`"$newCtl`" watchdog" -Force
        } catch {}
        Start-Process -FilePath $newCtl -ArgumentList "watchdog" -WindowStyle Hidden
    }
}

# 6. Send immediate heartbeat to Firebase Realtime Database so the web dashboard updates within 1 second
try {
    $roomId = "yeshiva-lab"
    $roomName = "ישיבת נשמת התורה - חדר מחשבים"
    $instName = "ישיבת נשמת התורה"
    $cfgPaths = @("$dataDir\App\config.ini", "$env:LOCALAPPDATA\SchoolFilter\config.ini", "C:\Program Files\SchoolFilter\config.ini")
    foreach ($cp in $cfgPaths) {
        if (Test-Path $cp) {
            foreach ($line in [IO.File]::ReadAllLines($cp, [Text.Encoding]::UTF8)) {
                if ($line -match "^RoomId=(.+)$") { $roomId = $Matches[1].Trim() }
                if ($line -match "^RoomName=(.+)$") { $roomName = $Matches[1].Trim() }
                if ($line -match "^InstitutionName=(.+)$") { $instName = $Matches[1].Trim() }
            }
            break
        }
    }

    $roomEnable = $true
    try {
        $roomJsonRaw = $wc.DownloadString("https://school-filter-2026-default-rtdb.firebaseio.com/rooms/$roomId.json")
        if ($roomJsonRaw -match '"filterEnabled"\s*:\s*false') { $roomEnable = $false }
    } catch {}

    $pcName = $env:COMPUTERNAME
    $uName = $env:USERNAME
    $docId = ($roomId -replace '[^a-zA-Z0-9_-]', '_') + "__" + ($pcName -replace '[^a-zA-Z0-9_-]', '_')
    $nowIso = [DateTime]::UtcNow.ToString("o")
    $hbObj = @{
        computerName = $pcName
        userName = $uName
        roomId = $roomId
        roomName = $roomName
        institutionName = $instName
        version = "3.4"
        filterActive = $roomEnable
        installedAt = $nowIso
        lastSeen = $nowIso
    } | ConvertTo-Json -Compress

    $hbUrl = "https://school-filter-2026-default-rtdb.firebaseio.com/computers/$docId.json"
    $hbBytes = [Text.Encoding]::UTF8.GetBytes($hbObj)
    $req = [Net.HttpWebRequest]::Create($hbUrl)
    $req.Method = "PUT"
    $req.Proxy = $null
    $req.Timeout = 4000
    $req.ContentType = "application/json; charset=utf-8"
    $req.ContentLength = $hbBytes.Length
    $reqStream = $req.GetRequestStream()
    $reqStream.Write($hbBytes, 0, $hbBytes.Length)
    $reqStream.Close()
    $resp = $req.GetResponse()
    $resp.Close()
} catch {}
