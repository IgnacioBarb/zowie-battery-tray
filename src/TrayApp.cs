using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ZowieBatteryTray
{
    // Icono de la bandeja: porcentaje sobre una insignia del color del nivel, rayo si carga, menú nativo.
    sealed class TrayApp : ApplicationContext
    {
        const int NormalIntervalMs = 5 * 60 * 1000;
        static readonly Color Grey = Color.FromArgb(0x5A, 0x5A, 0x5A);

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
        // Funciones sin nombre de uxtheme (Windows 10 1903+) que activan el tema oscuro de los menús nativos.
        [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);
        [DllImport("uxtheme.dll", EntryPoint = "#136")] static extern void FlushMenuThemes();

        readonly NotifyIcon notify = new NotifyIcon();
        readonly MenuItem autostartItem;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer quick = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer settle = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer exitCheck = new System.Windows.Forms.Timer();
        readonly DeviceChangeWatcher watcher = new DeviceChangeWatcher();
        readonly ChargeTrend trend = new ChargeTrend();
        readonly EventWaitHandle exitSignal;
        readonly FontFamily iconFont;
        readonly FontStyle iconFontStyle;

        ZowieDevice device;
        IntPtr iconHandle = IntPtr.Zero;
        bool lowWarned;
        int quickLeft;

        public TrayApp(EventWaitHandle exitSignal)
        {
            this.exitSignal = exitSignal;

            // Bahnschrift (Windows 10/11) es estrecha y se lee bien a 16 px; si faltara, Segoe UI en negrita.
            iconFont = new FontFamily("Segoe UI");
            iconFontStyle = FontStyle.Bold;
            using (var fonts = new InstalledFontCollection())
            {
                foreach (FontFamily family in fonts.Families)
                {
                    if (family.Name == "Bahnschrift SemiBold Condensed")
                    {
                        iconFont = new FontFamily(family.Name);
                        iconFontStyle = FontStyle.Regular;
                    }
                }
            }

            // Menú nativo de Windows (como el de HWiNFO): lo dibuja el sistema, sin iconos ni separadores.
            var refresh = new MenuItem("Actualizar ahora", delegate { UpdateBattery(); });
            refresh.DefaultItem = true;   // en negrita; es lo mismo que hace el doble clic
            autostartItem = new MenuItem("Iniciar con Windows", delegate { ToggleAutostart(); });
            var quit = new MenuItem("Salir", delegate { ExitThread(); });
            var menu = new ContextMenu(new[] { refresh, autostartItem, quit });
            menu.Popup += delegate
            {
                ApplyMenuTheme();
                autostartItem.Checked = Installer.IsAutostartEnabled();
            };
            notify.ContextMenu = menu;
            notify.DoubleClick += delegate { UpdateBattery(); };

            // Tras conectar o desconectar algo, y mientras el receptor aún no tenga datos del ratón,
            // se lee cada 15 s durante un minuto en vez de esperar a la siguiente lectura normal.
            quick.Interval = 15000;
            quick.Tick += delegate
            {
                if (--quickLeft <= 0) quick.Stop();
                UpdateBattery();
            };

            UpdateBattery();
            notify.Visible = true;

            timer.Interval = NormalIntervalMs;
            timer.Tick += delegate { UpdateBattery(); };
            timer.Start();

            // Al enchufar o quitar el cable (o el receptor) Windows manda varios avisos seguidos:
            // se espera a que paren 2 s y entonces se vuelve a buscar el dispositivo y a leer.
            settle.Interval = 2000;
            settle.Tick += delegate
            {
                settle.Stop();
                device = null;
                UpdateBattery();
                StartQuickReads();
            };
            watcher.Changed += delegate { settle.Stop(); settle.Start(); };

            // El instalador pide cerrar esta instancia antes de actualizarla o desinstalarla.
            exitCheck.Interval = 1000;
            exitCheck.Tick += delegate { if (this.exitSignal.WaitOne(0)) ExitThread(); };
            exitCheck.Start();
        }

        void StartQuickReads()
        {
            quickLeft = 4;
            quick.Stop();
            quick.Start();
        }

        void UpdateBattery()
        {
            try
            {
                if (device == null) device = ZowieHid.Find();
                if (device == null)
                {
                    SetState("?", Grey, Color.White, "Ratón Zowie: ni receptor ni cable conectados", false);
                    return;
                }
                BatteryReading reading;
                try { reading = Battery.Read(device); }
                catch (Exception)
                {
                    // Se desconectó o cambió de puerto: se busca de nuevo una vez.
                    device = ZowieHid.Find();
                    if (device == null) throw;
                    reading = Battery.Read(device);
                }

                string time = DateTime.Now.ToString("HH:mm");
                switch (reading.Status)
                {
                    case ReadStatus.Ok:
                        if (device.IsCable) trend.Reset();
                        else reading.Charging = trend.Update(reading.Millivolts) ? ChargeSource.Voltage : ChargeSource.None;
                        Color back, fore;
                        Battery.BandColors(reading.Millivolts, out back, out fore);
                        string status = reading.Charging == ChargeSource.Cable ? "Cargando por cable · " + time
                            : reading.Charging == ChargeSource.Voltage ? "Cargando · " + time
                            : "Actualizado a las " + time;
                        string tooltip = string.Format("Ratón Zowie: {0} % ({1:N2} V)\n{2}", reading.Percent, reading.Millivolts / 1000.0, status);
                        bool charging = reading.Charging != ChargeSource.None;
                        SetState(reading.Percent.ToString(), back, fore, tooltip, charging);
                        if (reading.Millivolts <= Battery.LowMillivolts && !charging && !lowWarned)
                        {
                            notify.ShowBalloonTip(10000, "Ratón Zowie", "Batería baja (" + reading.Percent + " %). Ponlo a cargar.", ToolTipIcon.Warning);
                            lowWarned = true;
                        }
                        else if (reading.Millivolts > Battery.QuarterMillivolts)
                        {
                            lowWarned = false;
                        }
                        break;
                    case ReadStatus.NoData:
                        SetState("?", Grey, Color.White, "Ratón Zowie: esperando datos del ratón (" + time + ")\n¿Apagado? Muévelo un poco.", false);
                        if (!quick.Enabled) StartQuickReads();
                        break;
                    case ReadStatus.NoReply:
                        SetState("?", Grey, Color.White, "Ratón Zowie: no responde (" + time + ")", false);
                        break;
                    default:
                        SetState("!", Grey, Color.White, "Ratón Zowie: respuesta inesperada (" + time + ")", false);
                        break;
                }
            }
            catch (Exception ex)
            {
                SetState("!", Grey, Color.White, "Ratón Zowie: error - " + ex.Message, false);
            }
        }

        void SetState(string text, Color back, Color fore, string tooltip, bool charging)
        {
            IntPtr handle = DrawIcon(text, back, fore, charging);
            notify.Icon = Icon.FromHandle(handle);
            if (iconHandle != IntPtr.Zero) DestroyIcon(iconHandle);
            iconHandle = handle;
            // El texto del icono admite como mucho 63 caracteres.
            notify.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
        }

        // Rayo de carga en coordenadas de 0 a 1 dentro de su caja.
        static readonly PointF[] BoltShape =
        {
            new PointF(0.62f, 0), new PointF(0, 0.56f), new PointF(0.42f, 0.56f), new PointF(0.30f, 1),
            new PointF(1, 0.40f), new PointF(0.56f, 0.40f), new PointF(0.80f, 0)
        };

        IntPtr DrawIcon(string text, Color back, Color fore, bool charging)
        {
            int size = SystemInformation.SmallIconSize.Width;
            float px = size / 16f;   // las medidas de abajo están pensadas para 16 px
            using (var bmp = new Bitmap(size, size))
            using (Graphics g = Graphics.FromImage(bmp))
            using (var foreBrush = new SolidBrush(fore))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                // Fondo: cuadrado con esquinas redondeadas (radio 3 px).
                float d = 6 * px;
                using (var path = new GraphicsPath())
                using (var backBrush = new SolidBrush(back))
                {
                    path.AddArc(0, 0, d, d, 180, 90);
                    path.AddArc(size - d, 0, d, d, 270, 90);
                    path.AddArc(size - d, size - d, d, d, 0, 90);
                    path.AddArc(0, size - d, d, d, 90, 90);
                    path.CloseFigure();
                    g.FillPath(backBrush, path);
                }

                // Zona del número: todo el ancho (1 px de margen) o, si carga, los 10 px de la izquierda
                // y el rayo a la derecha.
                float textLeft = 1 * px, textWidth, maxHeight;
                if (charging)
                {
                    textWidth = 10 * px; maxHeight = 10 * px;
                    var bolt = new PointF[BoltShape.Length];
                    for (int i = 0; i < bolt.Length; i++)
                        bolt[i] = new PointF((10.5f + 4.5f * BoltShape[i].X) * px, (3 + 10 * BoltShape[i].Y) * px);
                    g.FillPolygon(foreBrush, bolt);
                }
                else
                {
                    textWidth = 14 * px; maxHeight = 12 * px;
                }

                // Número como trazado, lo más grande que quepa. Su altura se redondea a un número de píxeles
                // que deje el mismo hueco arriba y abajo, y queda centrado: los bordes salen nítidos.
                using (var glyphs = new GraphicsPath())
                {
                    glyphs.AddString(text, iconFont, (int)iconFontStyle, size, PointF.Empty, StringFormat.GenericTypographic);
                    // Sin curvas, los límites son los del trazo real (con curvas incluirían sus puntos de control).
                    glyphs.Flatten();
                    RectangleF bounds = glyphs.GetBounds();
                    int height = (int)Math.Floor(Math.Min(maxHeight, bounds.Height * textWidth / bounds.Width));
                    if ((size - height) % 2 != 0) height--;
                    float scale = height / bounds.Height;
                    using (var matrix = new Matrix())
                    {
                        matrix.Translate(textLeft + (textWidth - bounds.Width * scale) / 2, (size - height) / 2f);
                        matrix.Scale(scale, scale);
                        matrix.Translate(-bounds.X, -bounds.Y);
                        glyphs.Transform(matrix);
                    }
                    g.FillPath(foreBrush, glyphs);
                }
                return bmp.GetHicon();
            }
        }

        // Tema de los menús nativos según el modo de las apps de Windows: oscuro o claro, como el de HWiNFO.
        static void ApplyMenuTheme()
        {
            object light = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            try
            {
                SetPreferredAppMode(light is int && (int)light == 0 ? 2 : 3);   // 2 = ForceDark, 3 = ForceLight
                FlushMenuThemes();
            }
            catch (EntryPointNotFoundException) { }   // Windows anterior a 10 1903: menú claro de siempre
        }

        void ToggleAutostart()
        {
            Installer.SetAutostart(!Installer.IsAutostartEnabled(), Application.ExecutablePath);
        }

        protected override void ExitThreadCore()
        {
            timer.Stop(); quick.Stop(); settle.Stop(); exitCheck.Stop();
            watcher.DestroyHandle();
            notify.Visible = false;
            notify.Dispose();
            if (iconHandle != IntPtr.Zero) { DestroyIcon(iconHandle); iconHandle = IntPtr.Zero; }
            base.ExitThreadCore();
        }
    }

    // Ventana oculta que avisa cuando se conecta o desconecta algún dispositivo (WM_DEVICECHANGE).
    sealed class DeviceChangeWatcher : NativeWindow
    {
        public event EventHandler Changed;

        public DeviceChangeWatcher() { CreateHandle(new CreateParams()); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0219 && Changed != null) Changed(this, EventArgs.Empty);
            base.WndProc(ref m);
        }
    }
}
