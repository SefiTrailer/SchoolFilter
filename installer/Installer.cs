using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SchoolFilter.Setup
{
    internal static class Program
    {
        private const string AppName = "SchoolFilter";
        private const string AppVersion = "1.0.0";
        private const string Publisher = "School IT Administration";
        private static readonly string TargetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 
            AppName
        );

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool isSilent = false;
            bool suppressMsgBoxes = false;
            bool isUninstall = false;

            foreach (string rawArg in args)
            {
                string arg = rawArg.Trim().ToUpperInvariant();
                if (arg == "/VERYSILENT" || arg == "/SILENT" || arg == "-S" || arg == "/S")
                {
                    isSilent = true;
                }
                else if (arg == "/SUPPRESSMSGBOXES" || arg == "-Q" || arg == "/Q")
                {
                    suppressMsgBoxes = true;
                }
                else if (arg == "/UNINSTALL" || arg == "-U" || arg == "/U")
                {
                    isUninstall = true;
                }
            }

            if (!IsAdministrator())
            {
                if (!suppressMsgBoxes)
                {
                    MessageBox.Show(
                        "Administrator privileges are required to run this operation.\nPlease run as Administrator.",
                        "SchoolFilter Setup - Permission Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
                return 5; // ERROR_ACCESS_DENIED
            }

            try
            {
                if (isUninstall)
                {
                    return PerformUninstall(isSilent, suppressMsgBoxes);
                }
                else
                {
                    return PerformInstall(isSilent, suppressMsgBoxes);
                }
            }
            catch (Exception ex)
            {
                if (!suppressMsgBoxes)
                {
                    MessageBox.Show(
                        "An error occurred during setup:\n\n" + ex.Message,
                        "SchoolFilter Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
                return 1;
            }
        }

        private static bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static int PerformInstall(bool isSilent, bool suppressMsgBoxes)
        {
            if (!isSilent)
            {
                DialogResult dr = MessageBox.Show(
                    "Welcome to the SchoolFilter Setup Wizard.\n\n" +
                    "This will install SchoolFilter (Whitelist-Only Internet Filter) into:\n" +
                    TargetDir + "\n\n" +
                    "Features:\n" +
                    " - Whitelist-only PAC filter (supports one-class.co.il, edu.gov.il, Google Classroom)\n" +
                    " - Veyon LAN bypass preservation\n" +
                    " - NTFS Read/Execute-only lockdown for standard users\n\n" +
                    "Do you want to proceed with the installation?",
                    "SchoolFilter Setup",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (dr != DialogResult.Yes)
                {
                    return 2; // Cancelled
                }
            }

            // 1. Create target directory
            if (!Directory.Exists(TargetDir))
            {
                Directory.CreateDirectory(TargetDir);
            }

            // 2. Extract embedded resources
            ExtractResource("filter.pac", Path.Combine(TargetDir, "filter.pac"));
            ExtractResource("BlockGames.bat", Path.Combine(TargetDir, "BlockGames.bat"));
            ExtractResource("AllowAll.bat", Path.Combine(TargetDir, "AllowAll.bat"));
            string configPath = Path.Combine(TargetDir, "config.ini");
            if (!File.Exists(configPath))
            {
                ExtractResource("config.ini", configPath);
            }

            // 3. Copy running executable as uninstaller
            string currentExePath = Assembly.GetExecutingAssembly().Location;
            string uninstallerPath = Path.Combine(TargetDir, "Uninstall.exe");
            if (!string.Equals(currentExePath, uninstallerPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(currentExePath, uninstallerPath, true);
            }

            // 4. Lock down NTFS ACLs:
            // Standard Users: Read & Execute ONLY (prevent modifying, deleting, bypassing)
            // Administrators & SYSTEM: Full Control
            ApplyStrictPermissions(TargetDir);

            // 5. Register in Windows Add/Remove Programs (Programs and Features)
            RegisterUninstallEntry(uninstallerPath);

            if (!isSilent && !suppressMsgBoxes)
            {
                MessageBox.Show(
                    "SchoolFilter has been successfully installed into:\n" + TargetDir + "\n\n" +
                    "Permissions locked: Standard users have Read & Execute access only.\n" +
                    "Ready for Veyon Master integration.",
                    "SchoolFilter Setup Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }

            return 0;
        }

        private static int PerformUninstall(bool isSilent, bool suppressMsgBoxes)
        {
            if (!isSilent)
            {
                DialogResult dr = MessageBox.Show(
                    "Are you sure you want to completely uninstall SchoolFilter and restore direct internet access?",
                    "SchoolFilter Uninstall",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );

                if (dr != DialogResult.Yes)
                {
                    return 2; // Cancelled
                }
            }

            // 1. Restore internet settings immediately for current user
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                {
                    if (key != null)
                    {
                        key.DeleteValue("AutoConfigURL", false);
                    }
                }
                // Refresh WinINet
                InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
            }
            catch {}

            // 2. Remove registry uninstallation entry
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SchoolFilter", false);
            }
            catch {}

            // 3. Clean up directory and self via detached process
            string batchCleanup = Path.Combine(Path.GetTempPath(), "SchoolFilter_Cleanup.bat");
            string cleanupScript = string.Format(
                "@echo off\r\n" +
                "ping 127.0.0.1 -n 3 > nul\r\n" +
                "rmdir /S /Q \"{0}\"\r\n" +
                "del \"%~f0\"\r\n",
                TargetDir
            );
            File.WriteAllText(batchCleanup, cleanupScript);

            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + batchCleanup + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);

            if (!isSilent && !suppressMsgBoxes)
            {
                MessageBox.Show(
                    "SchoolFilter has been successfully uninstalled.\nDirect internet access has been restored.",
                    "SchoolFilter Uninstalled",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }

            return 0;
        }

        private static void ExtractResource(string resourceName, string outputPath)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException("Embedded resource not found: " + resourceName);
                }

                using (FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.CopyTo(fs);
                }
            }
        }

        private static void ApplyStrictPermissions(string folderPath)
        {
            try
            {
                DirectoryInfo dInfo = new DirectoryInfo(folderPath);
                DirectorySecurity dSecurity = new DirectorySecurity();

                // Disable inheritance and preserve existing rules (as baseline)
                dSecurity.SetAccessRuleProtection(true, false);

                // Administrators - Full Control
                dSecurity.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow
                ));

                // SYSTEM - Full Control
                dSecurity.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow
                ));

                // Standard Users - Read & Execute ONLY (No Write, Delete, Modify)
                dSecurity.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                    FileSystemRights.ReadAndExecute,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow
                ));

                dInfo.SetAccessControl(dSecurity);

                // Also propagate to all existing files
                foreach (string filePath in Directory.GetFiles(folderPath))
                {
                    FileInfo fInfo = new FileInfo(filePath);
                    FileSecurity fSecurity = new FileSecurity();
                    fSecurity.SetAccessRuleProtection(true, false);

                    fSecurity.AddAccessRule(new FileSystemAccessRule(
                        new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                        FileSystemRights.FullControl,
                        AccessControlType.Allow
                    ));
                    fSecurity.AddAccessRule(new FileSystemAccessRule(
                        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                        FileSystemRights.FullControl,
                        AccessControlType.Allow
                    ));
                    fSecurity.AddAccessRule(new FileSystemAccessRule(
                        new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                        FileSystemRights.ReadAndExecute,
                        AccessControlType.Allow
                    ));

                    fInfo.SetAccessControl(fSecurity);
                }
            }
            catch
            {
                // Fallback to icacls if managed ACL fails
                ProcessStartInfo psi = new ProcessStartInfo("icacls.exe",
                    "\"" + folderPath + "\" /inheritance:r /grant:r \"BUILTIN\\Administrators:(OI)(CI)F\" \"NT AUTHORITY\\SYSTEM:(OI)(CI)F\" \"BUILTIN\\Users:(OI)(CI)RX\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (Process proc = Process.Start(psi))
                {
                    proc.WaitForExit();
                }
            }
        }

        private static void RegisterUninstallEntry(string uninstallerPath)
        {
            try
            {
                using (RegistryKey baseKey = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SchoolFilter"))
                {
                    if (baseKey != null)
                    {
                        baseKey.SetValue("DisplayName", "SchoolFilter (Classroom Internet Whitelist Filter)");
                        baseKey.SetValue("DisplayVersion", AppVersion);
                        baseKey.SetValue("Publisher", Publisher);
                        baseKey.SetValue("InstallLocation", TargetDir);
                        baseKey.SetValue("UninstallString", "\"" + uninstallerPath + "\" /UNINSTALL");
                        baseKey.SetValue("QuietUninstallString", "\"" + uninstallerPath + "\" /UNINSTALL /VERYSILENT /SUPPRESSMSGBOXES");
                        baseKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        baseKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch {}
        }
    }
}
