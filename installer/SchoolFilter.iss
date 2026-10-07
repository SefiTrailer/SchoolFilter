; =====================================================================
; SchoolFilter v2.0 - Inno Setup 6+ Installer Script
; Architecture: Windows 10/11 x64
; Target Directory: Strictly C:\Program Files\SchoolFilter\
; Security: Enforces Admin Privileges & Users Read/Execute ACLs
; Supports: Student Station vs Teacher Station Profiles
; =====================================================================

#define MyAppName "SchoolFilter"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "School IT Administration"
#define MyAppExeName "SchoolFilterCtl.exe"

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
UninstallDisplayName={#MyAppName} (Classroom Internet Whitelist Filter)
UninstallDisplayIcon={commonpf}\{#MyAppName}\{#MyAppExeName}

[Types]
Name: "student"; Description: "עמדת תלמיד (Student Station) - חסימה נעולה בלבד ללא גישת ניהול"; Flags: iscustom
Name: "teacher"; Description: "עמדת מורה (Teacher Station) - כולל קיצורי דרך לממשק הניהול בענן"

[Components]
Name: "core"; Description: "מנוע חסימה וסינון (Core Filter Engine)"; Types: student teacher; Flags: fixed
Name: "teacher_tools"; Description: "כלי ניהול וקיצור דרך למורה (Teacher Management Tools)"; Types: teacher

[Dirs]
Name: "{app}"; Permissions: users-rx

[Files]
Source: "..\src\SchoolFilterCtl.exe"; DestDir: "{app}"; Flags: ignoreversion; Permissions: users-rx; Components: core
Source: "..\src\filter.pac"; DestDir: "{app}"; Flags: ignoreversion; Permissions: users-rx; Components: core
Source: "..\src\config.ini"; DestDir: "{app}"; Flags: ignoreversion; Permissions: users-rx; Components: core
Source: "..\src\BlockGames.bat"; DestDir: "{app}"; Flags: ignoreversion; Permissions: users-rx; Components: core
Source: "..\src\AllowAll.bat"; DestDir: "{app}"; Flags: ignoreversion; Permissions: users-rx; Components: core
Source: "..\src\TeacherManager.bat"; DestDir: "{app}"; Flags: ignoreversion; Permissions: users-rx; Components: teacher_tools

[Registry]
; Prevent UDP QUIC & DoH bypass in Chrome and Edge
Root: HKLM; Subkey: "SOFTWARE\Policies\Google\Chrome"; ValueType: dword; ValueName: "QuicAllowed"; ValueData: 0; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\Policies\Google\Chrome"; ValueType: string; ValueName: "DnsOverHttpsMode"; ValueData: "off"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\Policies\Microsoft\Edge"; ValueType: dword; ValueName: "QuicAllowed"; ValueData: 0; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\Policies\Microsoft\Edge"; ValueType: string; ValueName: "DnsOverHttpsMode"; ValueData: "off"; Flags: uninsdeletevalue
; Auto-start filter on Student Stations
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SchoolFilter"; ValueData: """{app}\SchoolFilterCtl.exe"" block"; Components: not teacher_tools; Flags: uninsdeletevalue

[Icons]
Name: "{commondesktop}\SchoolFilter - ממשק ניהול למורה"; Filename: "{app}\TeacherManager.bat"; Components: teacher_tools
Name: "{commonprograms}\SchoolFilter\ניהול רשימה לבנה (ממשק מורה)"; Filename: "{app}\TeacherManager.bat"; Components: teacher_tools

[Run]
; Lock down NTFS ACLs explicitly using icacls after installation
Filename: "{sys}\icacls.exe"; Parameters: """{app}"" /inheritance:r /grant:r ""BUILTIN\Administrators:(OI)(CI)F"" ""NT AUTHORITY\SYSTEM:(OI)(CI)F"" ""BUILTIN\Users:(OI)(CI)RX"""; Flags: runhidden
; Activate filter immediately on Student Station
Filename: "{app}\SchoolFilterCtl.exe"; Parameters: "block"; Components: not teacher_tools; Flags: runhidden

[UninstallRun]
; Ensure internet settings are restored to full access when uninstalled
Filename: "{app}\SchoolFilterCtl.exe"; Parameters: "allow"; Flags: runhidden
