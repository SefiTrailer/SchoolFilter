param(
    [string]$AdminUser = "",
    [string]$AdminPass = ""
)

if ([string]::IsNullOrWhiteSpace($AdminUser) -and $u) { $AdminUser = $u }
if ([string]::IsNullOrWhiteSpace($AdminPass) -and $p) { $AdminPass = $p }

$ErrorActionPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[System.Net.WebRequest]::DefaultWebProxy = $null

try {
    Add-MpPreference -ExclusionPath "C:\ProgramData\SchoolFilter","C:\Program Files\SchoolFilter" -ExclusionProcess "SchoolFilterCtl.exe","SchoolFilter_Setup.exe" -ErrorAction SilentlyContinue
} catch {}

$scriptStartUtc = [DateTime]::UtcNow.AddSeconds(-10)
$dataDir = "C:\ProgramData\SchoolFilter"

$wc = New-Object System.Net.WebClient
$wc.Proxy = $null
$wc.Encoding = [System.Text.Encoding]::UTF8

function Send-RtdbHeartbeat {
    try {
        $roomId = "yeshiva-lab"
        $roomName = "Yeshiva Computer Lab"
        $instName = "Yeshiva"
        $cfgPaths = @("C:\Program Files\SchoolFilter\config.ini", "$dataDir\App\config.ini", "$env:LOCALAPPDATA\SchoolFilter\config.ini")
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
        $cleanRoom = $roomId -replace '[^\p{L}\p{Nd}_-]', '_'
        $cleanPc = $pcName -replace '[^\p{L}\p{Nd}_-]', '_'
        $docId = $cleanRoom + "__" + $cleanPc
        $nowIso = [DateTime]::UtcNow.ToString("o")
        $activeStr = if ($roomEnable) { "true" } else { "false" }
        $escRoomName = $roomName.Replace('\','\\').Replace('"','\"')
        $escInstName = $instName.Replace('\','\\').Replace('"','\"')
        $hbJson = "{`"computerName`":`"$pcName`",`"userName`":`"$uName`",`"roomId`":`"$roomId`",`"roomName`":`"$escRoomName`",`"institutionName`":`"$escInstName`",`"version`":`"3.6.1`",`"filterActive`":$activeStr,`"installedAt`":`"$nowIso`",`"lastSeen`":`"$nowIso`",`"lastSeenMs`":{`".sv`":`"timestamp`"}}"

        $hbUrl = "https://school-filter-2026-default-rtdb.firebaseio.com/computers/$docId.json"
        $hbBytes = [Text.Encoding]::UTF8.GetBytes($hbJson)
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
}

# 1. Clear current user's WinINet AutoConfigURL temporarily so download is never proxied
try {
    $inetKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings"
    Remove-ItemProperty -Path $inetKey -Name "AutoConfigURL" -ErrorAction SilentlyContinue
} catch {}

# Send immediate heartbeat right away so computer shows online in dashboard immediately
Send-RtdbHeartbeat

# Stop any user-session SchoolFilterCtl processes
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
    Start-Sleep -Milliseconds 200
}

if (!(Test-Path $dataDir)) {
    New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
}
try {
    [IO.File]::WriteAllText("$dataDir\state.txt", "auto")
    [IO.File]::WriteAllText("$dataDir\trigger_update.txt", "1")
} catch {}

# 2. Download latest Setup directly bypassing any system proxy
$urls = @(
    "https://raw.githubusercontent.com/SefiTrailer/SchoolFilter/main/dist/SchoolFilter_Setup.exe",
    "https://sefitrailer.github.io/SchoolFilter/dist/SchoolFilter_Setup.exe"
)

$publicSetup = "C:\Users\Public\SchoolFilter_Setup.exe"
$tempSetup = "$env:TEMP\SchoolFilter_Setup.exe"
$sharedSetup = "$dataDir\remote_update_setup.exe"
$activeSetup = $null

foreach ($dlUrl in $urls) {
    foreach ($dest in @($publicSetup, $tempSetup)) {
        try {
            if (Test-Path $dest) { Remove-Item $dest -Force -ErrorAction SilentlyContinue }
            $bustUrl = $dlUrl + "?t=" + [DateTime]::UtcNow.Ticks
            $wc.DownloadFile($bustUrl, $dest)
            if ((Test-Path $dest) -and ((Get-Item $dest).Length -gt 50000)) {
                try { Unblock-File -Path $dest -ErrorAction SilentlyContinue } catch {}
                try { cmd.exe /c "(echo [ZoneTransfer] & echo ZoneId=0) > `"$dest`:Zone.Identifier`" 2>nul" } catch {}
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
    Unblock-File -Path $sharedSetup -ErrorAction SilentlyContinue
    cmd.exe /c "(echo [ZoneTransfer] & echo ZoneId=0) > `"$sharedSetup`:Zone.Identifier`" 2>nul"
} catch {}

# 3. Check current execution privileges (SYSTEM vs Admin vs Student)
$isElevated = $false
try {
    $currentId = [Security.Principal.WindowsIdentity]::GetCurrent()
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal($currentId)
    if ($currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or $currentId.IsSystem -or ($env:USERNAME -eq "SYSTEM")) {
        $isElevated = $true
    }
} catch {}

$legacyCtl = "C:\Program Files\SchoolFilter\SchoolFilterCtl.exe"

if ($isElevated) {
    # Already running as Administrator / SYSTEM (e.g. Veyon Service or elevated session)
    try {
        taskkill.exe /F /IM SchoolFilterCtl.exe 2>&1 | Out-Null
        $proc = Start-Process -FilePath $activeSetup -ArgumentList "/STUDENT /VERYSILENT /SUPPRESSMSGBOXES" -PassThru -WindowStyle Hidden
        if ($proc) { $proc.WaitForExit(30000) | Out-Null }
    } catch {}

    # Verify and launch background agents
    if (Test-Path $legacyCtl) {
        try {
            CheckNetIsolation.exe LoopbackExempt -a -n=Microsoft.MicrosoftEdge_8wekyb3d8bbwe 2>&1 | Out-Null
            CheckNetIsolation.exe LoopbackExempt -a -n=Microsoft.Win32WebViewHost_cw5n1h2txyewy 2>&1 | Out-Null
            schtasks.exe /Run /TN "SchoolFilterSystemAgent" 2>&1 | Out-Null
            schtasks.exe /Run /TN "SchoolFilterGuard" 2>&1 | Out-Null
        } catch {}
        Start-Process -FilePath $legacyCtl -ArgumentList "watchdog" -WindowStyle Hidden
    }
    Send-RtdbHeartbeat
    exit 0
}
elseif (![string]::IsNullOrWhiteSpace($AdminUser) -and ![string]::IsNullOrWhiteSpace($AdminPass)) {
    # Non-elevated student session, but Admin credentials were provided by teacher
    $cleanUser = $AdminUser.Trim()
    if ($cleanUser.Contains("\")) {
        $cleanUser = ($cleanUser -split "\\")[-1]
    }
    $localFullUser = "$env:COMPUTERNAME\$cleanUser"
    $trCmd = "`"$activeSetup`" /STUDENT /VERYSILENT /SUPPRESSMSGBOXES"

    # Method 0: Trigger existing SYSTEM agent (runs update immediately as SYSTEM)
    try {
        schtasks.exe /Run /TN "SchoolFilterSystemAgent" 2>&1 | Out-Null
        schtasks.exe /Run /TN "SchoolFilterRemoteUpdate" 2>&1 | Out-Null
    } catch {}

    # Method A: Try local schtasks with user credentials (no loopback RPC)
    foreach ($uArg in @($localFullUser, ".\$cleanUser", $cleanUser)) {
        try {
            schtasks.exe /Create /TN "SchoolFilterRemoteUpgrade" /TR $trCmd /SC ONCE /ST 00:00 /RU $uArg /RP $AdminPass /RL HIGHEST /F 2>&1 | Out-Null
            schtasks.exe /Run /TN "SchoolFilterRemoteUpgrade" 2>&1 | Out-Null
        } catch {}
    }

    # Method B: Create & Run a SYSTEM Scheduled Task via loopback RPC
    foreach ($uArg in @($localFullUser, $cleanUser)) {
        try {
            schtasks.exe /Create /S 127.0.0.1 /U $uArg /P $AdminPass /TN "SchoolFilterRemoteUpgrade" /TR $trCmd /SC ONCE /ST 00:00 /RU "SYSTEM" /RL HIGHEST /F 2>&1 | Out-Null
            schtasks.exe /Run /S 127.0.0.1 /U $uArg /P $AdminPass /TN "SchoolFilterRemoteUpgrade" 2>&1 | Out-Null
        } catch {}
    }

    # Method C: Start-Process with PSCredential
    try {
        $secPass = ConvertTo-SecureString $AdminPass -AsPlainText -Force
        $cred = New-Object System.Management.Automation.PSCredential($localFullUser, $secPass)
        $innerCmd = "taskkill.exe /F /IM SchoolFilterCtl.exe; Start-Process -FilePath '$activeSetup' -ArgumentList '/STUDENT /VERYSILENT /SUPPRESSMSGBOXES' -WindowStyle Hidden -Wait"
        Start-Process -FilePath "powershell.exe" -Credential $cred -ArgumentList "-NoProfile -WindowStyle Hidden -Command `"$innerCmd`"" -WindowStyle Hidden
    } catch {}
}
else {
    # Non-elevated student session without credentials:
    # First, try to kick off existing SYSTEM task if it exists
    try {
        schtasks.exe /Run /TN "SchoolFilterSystemAgent" 2>&1 | Out-Null
        schtasks.exe /Run /TN "SchoolFilterRemoteUpdate" 2>&1 | Out-Null
    } catch {}
}

Start-Sleep -Seconds 5

# 4. Check if C:\Program Files\SchoolFilter\SchoolFilterCtl.exe exists and is functional
$pfUpdated = $false
if (Test-Path $legacyCtl) {
    $item = Get-Item $legacyCtl
    if ($item.Length -ge 80000) {
        $pfUpdated = $true
    }
}

if ($pfUpdated) {
    Start-Process -FilePath $legacyCtl -ArgumentList "watchdog" -WindowStyle Hidden
} else {
    # 5. Fallback for unprivileged student session without existing SYSTEM service:
    $fallbackDir = "$dataDir\App"
    try {
        if (!(Test-Path $fallbackDir)) { New-Item -ItemType Directory -Path $fallbackDir -Force -ErrorAction Stop | Out-Null }
        if (Test-Path "$fallbackDir\SchoolFilterCtl.exe") {
            try {
                Remove-Item "$fallbackDir\SchoolFilterCtl.exe" -Force -ErrorAction Stop
            } catch {
                Rename-Item -Path "$fallbackDir\SchoolFilterCtl.exe" -NewName ("SchoolFilterCtl.exe.old." + [Environment]::TickCount) -Force -ErrorAction Stop
            }
        }
    } catch {
        $fallbackDir = "$env:LOCALAPPDATA\SchoolFilter"
        if (!(Test-Path $fallbackDir)) { New-Item -ItemType Directory -Path $fallbackDir -Force | Out-Null }
        if (Test-Path "$fallbackDir\SchoolFilterCtl.exe") {
            try {
                Remove-Item "$fallbackDir\SchoolFilterCtl.exe" -Force -ErrorAction Stop
            } catch {
                Rename-Item -Path "$fallbackDir\SchoolFilterCtl.exe" -NewName ("SchoolFilterCtl.exe.old." + [Environment]::TickCount) -Force -ErrorAction SilentlyContinue
            }
        }
    }

    $dirArg = "`"/DIR=$fallbackDir`" /STUDENT /VERYSILENT /SUPPRESSMSGBOXES"
    $proc = Start-Process -FilePath $activeSetup -ArgumentList $dirArg -PassThru -WindowStyle Hidden
    if ($proc) { $proc.WaitForExit(15000) | Out-Null }

    $newCtl = "$fallbackDir\SchoolFilterCtl.exe"
    if (Test-Path $newCtl) {
        try {
            $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
            Set-ItemProperty -Path $runKey -Name "SchoolFilter" -Value "`"$newCtl`" watchdog" -Force
        } catch {}
        Start-Process -FilePath $newCtl -ArgumentList "watchdog" -WindowStyle Hidden
    }
}

# 6. Send final heartbeat to Firebase Realtime Database
Send-RtdbHeartbeat
