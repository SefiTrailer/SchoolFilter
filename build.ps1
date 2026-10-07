<#
.SYNOPSIS
    Builds the standalone SchoolFilter_Setup.exe installer (v2.0).
.DESCRIPTION
    Compiles SchoolFilterCtl.exe native controller and embeds it into SchoolFilter_Setup.exe using:
    1. Built-in Windows C# compiler (csc.exe), OR
    2. Inno Setup Compiler (ISCC.exe) if requested.
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

if (-not $FoundCsc) {
    throw "C# compiler (csc.exe) not found on this Windows system."
}

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host " Building SchoolFilter v2.0 Controller & Installer" -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan

# Step 1: Compile SchoolFilterCtl.exe (Native WinINet + Sinkhole Controller)
$CtlSource = Join-Path $SrcDir "SchoolFilterCtl.cs"
$CtlExe = Join-Path $SrcDir "SchoolFilterCtl.exe"
Write-Host "[1/2] Compiling native controller (SchoolFilterCtl.exe)..." -ForegroundColor Yellow

$CtlArgs = @(
    "/target:winexe",
    "/optimize+",
    "/platform:anycpu",
    "/out:$CtlExe",
    "/reference:System.dll,System.Core.dll",
    "$CtlSource"
)
& "$FoundCsc" $CtlArgs
if ($LASTEXITCODE -ne 0) {
    throw "SchoolFilterCtl compilation failed with exit code $LASTEXITCODE"
}

# Step 2: Compile SchoolFilter_Setup.exe
Write-Host "[2/2] Compiling standalone installer (SchoolFilter_Setup.exe)..." -ForegroundColor Yellow

$ManifestPath = Join-Path $InstallerDir "app.manifest"
$SourceCs = Join-Path $InstallerDir "Installer.cs"
$CtlRes = $CtlExe + ",SchoolFilterCtl.exe"
$PacRes = (Join-Path $SrcDir "filter.pac") + ",filter.pac"
$ConfigRes = (Join-Path $SrcDir "config.ini") + ",config.ini"
$BlockRes = (Join-Path $SrcDir "BlockGames.bat") + ",BlockGames.bat"
$AllowRes = (Join-Path $SrcDir "AllowAll.bat") + ",AllowAll.bat"
$TeacherRes = (Join-Path $SrcDir "TeacherManager.bat") + ",TeacherManager.bat"

$CscArgs = @(
    "/target:winexe",
    "/optimize+",
    "/platform:anycpu",
    "/out:$OutputExe",
    "/win32manifest:$ManifestPath",
    "/reference:System.Windows.Forms.dll,System.Drawing.dll,System.dll,System.Core.dll",
    "/resource:$CtlRes",
    "/resource:$PacRes",
    "/resource:$ConfigRes",
    "/resource:$BlockRes",
    "/resource:$AllowRes",
    "/resource:$TeacherRes",
    "$SourceCs"
)

& "$FoundCsc" $CscArgs
if ($LASTEXITCODE -ne 0) {
    throw "Installer compilation failed with exit code $LASTEXITCODE"
}

if (Test-Path $OutputExe) {
    $FileItem = Get-Item $OutputExe
    $Hash = (Get-FileHash $OutputExe -Algorithm SHA256).Hash
    Write-Host "`n[SUCCESS] Installer v2.0 built successfully!" -ForegroundColor Green
    Write-Host " Output Binary: $($FileItem.FullName)" -ForegroundColor White
    Write-Host " Size: $([math]::Round($FileItem.Length / 1KB, 2)) KB" -ForegroundColor White
    Write-Host " SHA256: $Hash" -ForegroundColor Gray
    Write-Host "`nSilent Installation Syntax:" -ForegroundColor Cyan
    Write-Host " Student PC: `"$($FileItem.FullName)`" /STUDENT /VERYSILENT /SUPPRESSMSGBOXES" -ForegroundColor Yellow
    Write-Host " Teacher PC: `"$($FileItem.FullName)`" /TEACHER /VERYSILENT /SUPPRESSMSGBOXES" -ForegroundColor Yellow
} else {
    throw "Output file was not generated: $OutputExe"
}
