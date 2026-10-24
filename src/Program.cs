using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Zowie Battery Tray")]
[assembly: AssemblyDescription("Batería del ratón Zowie (serie DW) en la bandeja del sistema")]
[assembly: AssemblyProduct("Zowie Battery Tray")]
[assembly: AssemblyCompany("IgnacioBarb")]
[assembly: AssemblyCopyright("Copyright (c) 2026 IgnacioBarb. Licencia MIT.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

namespace ZowieBatteryTray
{
    static class Program
    {
        public const string MutexName = @"Local\ZowieBatteryTray";
        public const string ExitEventName = @"Local\ZowieBatteryTray.Exit";

        // Sin argumentos: si no es la copia instalada, ofrece instalarla.
        // /portable: abre el icono sin instalar.  /uninstall: desinstala (lo usa Configuración > Aplicaciones).
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (HasArg(args, "/uninstall"))
            {
                Installer.Uninstall();
                return;
            }
            if (!Installer.IsRunningInstalledCopy() && !HasArg(args, "/portable"))
            {
                DialogResult answer = MessageBox.Show(
                    "¿Quieres instalar " + Installer.AppName + "?\n\n" +
                    "Se copiará a tu carpeta de programas (sin permisos de administrador), tendrá acceso en el menú " +
                    "Inicio, se abrirá al iniciar Windows y podrás desinstalarlo desde Configuración > Aplicaciones.\n\n" +
                    "Sí: instalar    No: abrirlo solo esta vez",
                    Installer.AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Yes) { Installer.Install(); return; }
                if (answer != DialogResult.No) return;
            }

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    MessageBox.Show(Installer.AppName + " ya está abierto. Busca su icono junto al reloj (o en la flechita ^).",
                        Installer.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                using (var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName))
                    Application.Run(new TrayApp(exitSignal));
            }
        }

        static bool HasArg(string[] args, string name)
        {
            foreach (string a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
