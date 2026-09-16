<#
.SYNOPSIS
    Builds the standalone SchoolFilter_Setup.exe installer.
.DESCRIPTION
    Compiles SchoolFilter setup executable using:
    1. Built-in Windows C# compiler (csc.exe) with embedded PAC and scripts, OR
    2. Inno Setup Compiler (ISCC.exe) if available.
#>

param (
    [ValidateSet("Auto", "CSharp", "InnoSetup")]
    [string]$TargetCompiler = "Auto"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$SrcDir = Join-Path $ProjectRoot "src"
$InstallerDir = Join-Path $ProjectRoot "installer"
$DistDir = Join-Path $ProjectRoot "dist"

if (-not (Test-Path $DistDir)) {
    New-Item -ItemType Directory -Path $DistDir | Out-Null
}

$OutputExe = Join-Path $DistDir "SchoolFilter_Setup.exe"

# Locate Inno Setup Compiler if present
$IsccPaths = @(
    "ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$FoundIscc = $null
foreach ($path in $IsccPaths) {
    if (Get-Command $path -ErrorAction SilentlyContinue) {
        $FoundIscc = (Get-Command $path).Source
        break
    } elseif (Test-Path $path) {
        $FoundIscc = $path
        break
    }
}

# Locate csc.exe (Standard .NET Framework on Windows 10/11)
$CscPaths = @(
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$FoundCsc = $null
foreach ($path in $CscPaths) {
    if (Test-Path $path) {
        $FoundCsc = $path
        break
    }
}

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host " Building SchoolFilter Standalone Installer" -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan

$CompileWithInno = $false
if ($TargetCompiler -eq "InnoSetup") {
    if (-not $FoundIscc) {
        throw "Inno Setup compiler (ISCC.exe) was not found. Please install Inno Setup 6 or use -TargetCompiler CSharp."
    }
    $CompileWithInno = $true
} elseif ($TargetCompiler -eq "CSharp") {
    $CompileWithInno = $false
} else {
    # Auto: Prefer CSharp standalone compiler as it requires zero external dependencies,
    # or Inno if available. Let's build with CSharp so it produces the standalone exe right away.
    if ($FoundIscc) {
        $CompileWithInno = $true
    } else {
        $CompileWithInno = $false
    }
}

if ($CompileWithInno) {
    Write-Host "[*] Compiling using Inno Setup 6: $FoundIscc" -ForegroundColor Yellow
    $IssPath = Join-Path $InstallerDir "SchoolFilter.iss"
    & "$FoundIscc" "$IssPath"
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
    }
} else {
    if (-not $FoundCsc) {
        throw "C# compiler (csc.exe) not found on this Windows system."
    }
    Write-Host "[*] Compiling standalone executable using C# compiler ($FoundCsc)..." -ForegroundColor Yellow
    
    $ManifestPath = Join-Path $InstallerDir "app.manifest"
    $SourceCs = Join-Path $InstallerDir "Installer.cs"
    $PacRes = (Join-Path $SrcDir "filter.pac") + ",filter.pac"
    $ConfigRes = (Join-Path $SrcDir "config.ini") + ",config.ini"
    $BlockRes = (Join-Path $SrcDir "BlockGames.bat") + ",BlockGames.bat"
    $AllowRes = (Join-Path $SrcDir "AllowAll.bat") + ",AllowAll.bat"

    $CscArgs = @(
        "/target:winexe",
        "/optimize+",
        "/platform:anycpu",
        "/out:$OutputExe",
        "/win32manifest:$ManifestPath",
        "/reference:System.Windows.Forms.dll,System.dll,System.Core.dll",
        "/resource:$PacRes",
        "/resource:$ConfigRes",
        "/resource:$BlockRes",
        "/resource:$AllowRes",
        "$SourceCs"
    )

    & "$FoundCsc" $CscArgs
    if ($LASTEXITCODE -ne 0) {
        throw "CSC compilation failed with exit code $LASTEXITCODE"
    }
}

if (Test-Path $OutputExe) {
    $FileItem = Get-Item $OutputExe
    $Hash = (Get-FileHash $OutputExe -Algorithm SHA256).Hash
    Write-Host "`n[SUCCESS] Installer built successfully!" -ForegroundColor Green
    Write-Host " Output Binary: $($FileItem.FullName)" -ForegroundColor White
    Write-Host " Size: $([math]::Round($FileItem.Length / 1KB, 2)) KB" -ForegroundColor White
    Write-Host " SHA256: $Hash" -ForegroundColor Gray
    Write-Host "`nSilent Installation Syntax (for SCCM, Intune, Veyon, or batch scripts):" -ForegroundColor Cyan
    Write-Host " `"$($FileItem.FullName)`" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART" -ForegroundColor Yellow
} else {
    throw "Output file was not generated: $OutputExe"
}
