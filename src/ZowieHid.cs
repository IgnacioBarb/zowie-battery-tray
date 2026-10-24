using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ZowieBatteryTray
{
    // Receptor o ratón conectado por cable con el canal privado de Zowie (informes HID 0x08 y 0x09).
    sealed class ZowieDevice
    {
        public ushort ProductId;
        public bool IsCable;          // el propio ratón conectado por cable, que por tanto está cargando
        public string CommandPath;    // colección 0xFF03: informe de salida 0x08 de 16 bytes
        public string ReplyPath;      // colección 0xFF04: informe de entrada 0x09 de 16 bytes
    }

    // Protocolo sacado de los firmwares oficiales (herramienta "FW update tool for ZOWIE DW mouse" v1.0.21.0).
    // El receptor (PID 800A) y el ratón por cable (PID 800D en el EC-DW) responden igual al único comando
    // que se envía:  08 B4 15 00 ... (16 bytes). Es de solo lectura: devuelve 09 HH LL 00 C8 ..., donde HHLL
    // es la tensión de la batería en centésimas de voltio.
    static class ZowieHid
    {
        const ushort VendorId = 0x04A5;
        // PID de los receptores DW según su firmware. Con cualquier otro PID, el canal es el del ratón por cable.
        static readonly ushort[] ReceiverProductIds = { 0x800A };

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HiddAttributes attributes);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out HidpCaps caps);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref DeviceInterfaceData data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DeviceInterfaceData data, IntPtr detail, int size, out int required, IntPtr info);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(SafeFileHandle h, byte[] buffer, uint count, out uint written, IntPtr overlapped);

        [StructLayout(LayoutKind.Sequential)]
        struct HiddAttributes { public int Size; public ushort VendorID, ProductID, VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        struct HidpCaps
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DeviceInterfaceData { public int Size; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
        const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, SHARE_READ_WRITE = 3, OPEN_EXISTING = 3;
        const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        static IEnumerable<string> HidDevicePaths()
        {
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);
            IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == new IntPtr(-1)) yield break;
            try
            {
                var data = new DeviceInterfaceData();
                data.Size = Marshal.SizeOf(typeof(DeviceInterfaceData));
                for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref data); i++)
                {
                    int required;
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    IntPtr detail = Marshal.AllocHGlobal(required);
                    try
                    {
                        // cbSize de SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 en 64 bits, 6 en 32 bits.
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out required, IntPtr.Zero))
                            yield return Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }

        // Si están el receptor y el ratón por cable, se prefiere el ratón: su tensión es la actual
        // (el receptor ya no recibe nada) y además indica que está cargando.
        public static ZowieDevice Find()
        {
            var devices = new Dictionary<ushort, ZowieDevice>();
            foreach (string path in HidDevicePaths())
            {
                if (path.IndexOf("vid_04a5", StringComparison.OrdinalIgnoreCase) < 0) continue;
                using (SafeFileHandle h = CreateFile(path, 0, SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) continue;
                    var attributes = new HiddAttributes();
                    attributes.Size = Marshal.SizeOf(typeof(HiddAttributes));
                    if (!HidD_GetAttributes(h, ref attributes) || attributes.VendorID != VendorId) continue;
                    IntPtr preparsed;
                    if (!HidD_GetPreparsedData(h, out preparsed)) continue;
                    HidpCaps caps;
                    try { HidP_GetCaps(preparsed, out caps); }
                    finally { HidD_FreePreparsedData(preparsed); }

                    ZowieDevice device;
                    if (!devices.TryGetValue(attributes.ProductID, out device))
                    {
                        device = new ZowieDevice();
                        device.ProductId = attributes.ProductID;
                        device.IsCable = Array.IndexOf(ReceiverProductIds, attributes.ProductID) < 0;
                        devices[attributes.ProductID] = device;
                    }
                    if (caps.UsagePage == 0xFF03 && caps.OutputReportByteLength == 16) device.CommandPath = path;
                    if (caps.UsagePage == 0xFF04 && caps.InputReportByteLength == 16) device.ReplyPath = path;
                }
            }
            ZowieDevice best = null;
            foreach (ZowieDevice d in devices.Values)
            {
                if (d.CommandPath == null || d.ReplyPath == null) continue;
                if (best == null || (d.IsCable && !best.IsCable)) best = d;
            }
            return best;
        }

        // Envía el comando 0x15 y devuelve la respuesta (informe 0x09), o null si no llega a tiempo.
        public static byte[] ReadBatteryReport(ZowieDevice device, int timeoutMs)
        {
            var request = new byte[16];
            request[0] = 0x08; request[1] = 0xB4; request[2] = 0x15;

            SafeFileHandle replies = CreateFile(device.ReplyPath, GENERIC_READ, SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (replies.IsInvalid) throw new IOException("No se pudo abrir el canal de respuestas (" + Marshal.GetLastWin32Error() + ")");
            using (var stream = new FileStream(replies, FileAccess.Read, 1, true))
            using (SafeFileHandle commands = CreateFile(device.CommandPath, GENERIC_WRITE, SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (commands.IsInvalid) throw new IOException("No se pudo abrir el canal de comandos (" + Marshal.GetLastWin32Error() + ")");
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                var buffer = new byte[16];
                // La lectura se arranca antes de enviar para no perder la respuesta.
                var pending = stream.ReadAsync(buffer, 0, 16);
                uint written;
                if (!WriteFile(commands, request, 16, out written, IntPtr.Zero))
                    throw new IOException("No se pudo enviar el comando (" + Marshal.GetLastWin32Error() + ")");
                while (true)
                {
                    TimeSpan left = deadline - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero || !pending.Wait(left)) return null;
                    if (pending.Result > 0 && buffer[0] == 0x09) return buffer;
                    buffer = new byte[16];
                    pending = stream.ReadAsync(buffer, 0, 16);
                }
            }
        }
    }
}
