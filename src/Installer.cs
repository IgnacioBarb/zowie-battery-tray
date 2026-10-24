using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ZowieBatteryTray
{
    // Instalación solo para el usuario actual, sin permisos de administrador:
    // %LOCALAPPDATA%\Programs\ZowieBatteryTray, acceso en el menú Inicio, arranque con Windows
    // y entrada en Configuración > Aplicaciones para desinstalarla.
    static class Installer
    {
        public const string AppName = "Zowie Battery Tray";
        public const string Publisher = "IgnacioBarb";
        public const string ProjectUrl = "https://github.com/IgnacioBarb/zowie-battery-tray";
        const string ExeName = "ZowieBatteryTray.exe";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "ZowieBatteryTray";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ZowieBatteryTray";

        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ZowieBatteryTray"); }
        }

        static string InstalledExe { get { return Path.Combine(InstallDir, ExeName); } }

        static string StartMenuShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"); }
        }

        public static bool IsRunningInstalledCopy()
        {
            return string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);
        }

        public static void Install()
        {
            try
            {
                CloseRunningInstance();
                Directory.CreateDirectory(InstallDir);
                CopyWithRetry(Application.ExecutablePath, InstalledExe);
                CreateShortcut(StartMenuShortcut, InstalledExe);
                SetAutostart(true, InstalledExe);
                WriteUninstallEntry();
                Process.Start(InstalledExe);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo instalar: " + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static void Uninstall()
        {
            if (MessageBox.Show("¿Desinstalar " + AppName + "?", AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            CloseRunningInstance();
            SetAutostart(false, null);
            try { File.Delete(StartMenuShortcut); } catch (IOException) { }
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            MessageBox.Show(AppName + " se ha desinstalado.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            // Este mismo .exe puede ser el instalado: la carpeta se borra un par de segundos después,
            // cuando este proceso ya ha terminado (por eso el aviso va antes).
            var cleanup = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + InstallDir + "\"");
            cleanup.CreateNoWindow = true;
            cleanup.UseShellExecute = false;
            Process.Start(cleanup);
        }

        public static bool IsAutostartEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue(RunValue) != null;
        }

        public static void SetAutostart(bool enabled, string exePath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(RunValue, "\"" + exePath + "\"");
                else key.DeleteValue(RunValue, false);
            }
        }

        // Pide a la instancia abierta que se cierre (para poder sobrescribir el .exe) y espera a que lo haga.
        static void CloseRunningInstance()
        {
            EventWaitHandle exitSignal;
            if (!EventWaitHandle.TryOpenExisting(Program.ExitEventName, out exitSignal)) return;
            using (exitSignal) exitSignal.Set();
            for (int i = 0; i < 50; i++)
            {
                Mutex running;
                if (!Mutex.TryOpenExisting(Program.MutexName, out running)) return;
                running.Dispose();
                Thread.Sleep(100);
            }
        }

        static void CopyWithRetry(string source, string destination)
        {
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) return;
            for (int attempt = 1; ; attempt++)
            {
                try { File.Copy(source, destination, true); return; }
                catch (IOException)
                {
                    if (attempt == 10) throw;
                    Thread.Sleep(300);
                }
            }
        }

        static void CreateShortcut(string shortcutPath, string target)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                try
                {
                    Type t = shortcut.GetType();
                    t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { target });
                    t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(target) });
                    t.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Batería del ratón Zowie en la bandeja del sistema" });
                    t.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                }
                finally { Marshal.FinalReleaseComObject(shortcut); }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }

        static void WriteUninstallEntry()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", Application.ProductVersion);
                key.SetValue("Publisher", Publisher);
                key.SetValue("URLInfoAbout", ProjectUrl);
                key.SetValue("DisplayIcon", InstalledExe);
                key.SetValue("InstallLocation", InstallDir);
                key.SetValue("UninstallString", "\"" + InstalledExe + "\" /uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024) + 1, RegistryValueKind.DWord);
            }
        }
    }
}
