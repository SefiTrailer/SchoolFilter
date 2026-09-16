# SchoolFilter - Classroom Internet Access Controller for Veyon

Lightweight, high-security Windows IT-admin tool designed for school computer labs managed via **Veyon Classroom Management Software**. Allows teachers to switch student internet access between **"Strict Whitelist Only"** (blocking unapproved sites, games, proxies) and **"Full Internet Access"** with a single click in Veyon Master.

---

## 📁 Project Structure

```text
c:\Users\Sefi\projects\Block Web\
├── src\
│   ├── filter.pac          # Proxy Auto-Configuration script with educational whitelist & sinkhole
│   ├── BlockGames.bat      # Instant lockdown script (enforces PAC via Registry)
│   └── AllowAll.bat        # Instant release script (clears PAC, restores direct access)
├── installer\
│   ├── SchoolFilter.iss    # Inno Setup 6+ compiler script (Admin UAC + ACL hardening)
│   ├── Installer.cs        # Standalone native C# installer (zero-dependency, built with csc.exe)
│   └── app.manifest        # UAC requireAdministrator manifest
├── dist\
│   └── SchoolFilter_Setup.exe # Compiled standalone standalone installer (16 KB)
├── build.ps1               # Automated build script (supports C# csc.exe and Inno Setup 6)
└── README.md               # Complete documentation and Veyon setup guide
```

---

## 🛡️ Architecture & Security Model

1. **Target Installation Directory**: Strictly `C:\Program Files\SchoolFilter\`.
2. **NTFS ACL Lockdown**:
   - `BUILTIN\Administrators`: **Full Control**
   - `NT AUTHORITY\SYSTEM`: **Full Control**
   - `BUILTIN\Users` (Students): **Read & Execute Only**
   - Inheritance is stripped; students cannot modify, rename, replace, or delete `filter.pac` or the scripts.
3. **Veyon LAN Preservation**:
   - `filter.pac` automatically bypasses local hostnames, loopbacks (`127.0.0.1`), and private subnets (`10.*`, `192.168.*`, `172.16-31.*`).
   - **Veyon Master communication (ports 11100, 11400) remains 100% active and immune to filter toggling.**
4. **Whitelisted Domains**:
   - `one-class.co.il` & `*.one-class.co.il`
   - `*.edu.gov.il` & `edu.gov.il`
   - `*.education.gov.il` & `education.gov.il`
   - `classroom.google.com`
   - Essential Google authentication and assets (`accounts.google.com`, `ssl.gstatic.com`, `fonts.googleapis.com`, `apis.google.com`, `drive.google.com`, `docs.google.com`).
   - All unapproved traffic (online games, social media, web proxies) is redirected to `127.0.0.1:9999` (dummy sinkhole).

---

## 🚀 Installation

### Option A: Interactive Installation
Run `dist\SchoolFilter_Setup.exe` as Administrator and follow the wizard.

### Option B: Silent / Mass Deployment (SCCM, Intune, Batch, PDQ Deploy)
```cmd
"SchoolFilter_Setup.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

---

## 🖥️ Veyon Master Setup Guide (הגדרת Veyon למורה)

To add the two buttons to Veyon Master so any teacher can toggle the internet with one click:

### 1. Open Veyon Configurator (on Teacher and/or Master PC):
1. Navigate to: **Master** -> **Predefined programs** (תוכניות מוגדרות מראש).
2. Click **Add** (+) to create the first button:
   - **Name (שם):** `🔒 חסימת משחקים (רשימה לבנה בלבד)`
   - **Command line (פקודה):** `cmd.exe /c "C:\Program Files\SchoolFilter\BlockGames.bat"`
3. Click **Add** (+) to create the second button:
   - **Name (שם):** `🔓 פתיחת אינטרנט מלא`
   - **Command line (פקודה):** `cmd.exe /c "C:\Program Files\SchoolFilter\AllowAll.bat"`
4. Click **Apply** (החל) and close Veyon Configurator.

### 2. Daily Classroom Use:
- In **Veyon Master**, select all computers (or individual students).
- In the top toolbar, click **Run program** (הפעל תוכנית) and choose:
  - `🔒 חסימת משחקים` when you want students focused on study sites.
  - `🔓 פתיחת אינטרנט מלא` at the end of the lesson or when needed.

---

## 🔨 How to Rebuild Installer

Run the automated PowerShell build script:
```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -TargetCompiler Auto
```
- Uses built-in Windows C# compiler (`csc.exe`) automatically.
- If Inno Setup 6 (`ISCC.exe`) is installed, passing `-TargetCompiler InnoSetup` compiles `SchoolFilter.iss`.
