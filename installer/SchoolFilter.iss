; =====================================================================
; SchoolFilter - Inno Setup 6+ Installer Script
; Architecture: Windows 10/11 x64
; Target Directory: Strictly C:\Program Files\SchoolFilter\
; Security: Enforces Admin Privileges & Users Read/Execute ACLs
; =====================================================================

#define MyAppName "SchoolFilter"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "School IT Administration"
#define MyAppExeName "BlockGames.bat"

[Setup]
AppId={{D3A56D82-3A68-4C8E-98BF-281C30E30489}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={commonpf}\{#MyAppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=SchoolFilter_Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppName} (Internet Classroom Filter)
UninstallDisplayIcon={commonpf}\{#MyAppName}\{#MyAppExeName}

[Dirs]
Name: "{app}"; Permissions: users-rx

[Files]
Source: "..\src\filter.pac"; DestDir: "{app}"; Flags: ignoreversion; Permissions: users-rx
Source: "..\src\config.ini"; DestDir: "{app}"; Flags: ignoreversion; Permissions: users-rx
Source: "..\src\BlockGames.bat"; DestDir: "{app}"; Flags: ignoreversion; Permissions: users-rx
Source: "..\src\AllowAll.bat"; DestDir: "{app}"; Flags: ignoreversion; Permissions: users-rx

[Run]
; Lock down NTFS ACLs explicitly using icacls after installation:
; - Administrators: Full Control (F)
; - SYSTEM: Full Control (F)
; - Standard Users: Read & Execute only (RX) - No Write, Modify, or Delete
Filename: "{sys}\icacls.exe"; Parameters: """{app}"" /inheritance:r /grant:r ""BUILTIN\Administrators:(OI)(CI)F"" ""NT AUTHORITY\SYSTEM:(OI)(CI)F"" ""BUILTIN\Users:(OI)(CI)RX"""; Flags: runhidden

[UninstallRun]
; Ensure internet settings are restored to full access when uninstalled
Filename: "{app}\AllowAll.bat"; Flags: runhidden
