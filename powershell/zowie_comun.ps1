<#
  Funciones compartidas para leer la batería de un ratón Zowie de la serie DW (EC2-DW, etc.)
  a través de su receptor o del propio ratón conectado por cable.
  Se cargan con:  . (Join-Path $PSScriptRoot 'zowie_comun.ps1')

  Protocolo (sacado de los firmwares oficiales, herramienta v1.0.21.0): el receptor (PID 800A) y el
  ratón por cable (PID 800D en el EC-DW) tienen el mismo canal privado. Se envía UN único comando
  (informe HID 0x08):
      08 B4 15 00 00 00 00 00 00 00 00 00 00 00 00 00
  El comando 0x15 solo lee la tensión de la batería; no escribe nada ni cambia ajustes.
  La respuesta llega por el informe 0x09:
      09 HH LL 00 C8 ...      HHLL = tensión de la batería en centésimas de voltio (big-endian)

  Carga: con el cable al PC el ratón pasa a modo cable y se carga, así que aparecer como dispositivo
  USB propio significa que está cargando. Por radio el ratón solo envía la tensión; por encima de
  4,25 V tiene que estar en la base o en un cargador, porque en reposo una batería de litio no pasa de 4,2 V.

  El porcentaje es una estimación: se interpola entre los umbrales que usa el propio ratón
  para su LED (3,68 V = 10 %, 3,76 V = 25 %, 3,89 V = 50 %) y valores típicos de una batería
  de litio (4,00 V = 75 %, 4,20 V = 100 %, 3,50 V = 0 %). Mientras carga, la tensión sale más alta.
#>

if (-not ('ZowieHid' -as [type])) {
  Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class ZowieHid {
  [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
  [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
  [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
  [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out Caps c);
  [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
  static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
  [DllImport("kernel32.dll", SetLastError = true)]
  static extern bool WriteFile(SafeFileHandle h, byte[] b, uint n, out uint w, IntPtr o);

  [StructLayout(LayoutKind.Sequential)] public struct Caps {
    public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
    public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
      NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
      NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
  }

  const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_FLAG_OVERLAPPED = 0x40000000;

  static string PathFor(string instanceId) {
    Guid g; HidD_GetHidGuid(out g);
    return @"\\?\" + instanceId.Replace('\\', '#') + "#{" + g.ToString() + "}";
  }

  // { UsagePage, InputReportByteLength, OutputReportByteLength } o null si no se puede abrir.
  public static int[] GetCaps(string instanceId) {
    using (var h = CreateFile(PathFor(instanceId), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
      if (h.IsInvalid) return null;
      IntPtr p;
      if (!HidD_GetPreparsedData(h, out p)) return null;
      try {
        Caps c; HidP_GetCaps(p, out c);
        return new int[] { c.UsagePage, c.InputReportByteLength, c.OutputReportByteLength };
      } finally { HidD_FreePreparsedData(p); }
    }
  }

  // Único comando que se envía: 0x15, lectura de la tensión de la batería.
  public static byte[] ReadBatteryReport(string outId, string inId, int timeoutMs) {
    var request = new byte[16];
    request[0] = 0x08; request[1] = 0xB4; request[2] = 0x15;

    var hin = CreateFile(PathFor(inId), GENERIC_READ, 3, IntPtr.Zero, 3, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
    if (hin.IsInvalid) throw new IOException("No se pudo abrir el canal de respuestas (" + Marshal.GetLastWin32Error() + ")");
    using (var fin = new FileStream(hin, FileAccess.Read, 1, true))
    using (var hout = CreateFile(PathFor(outId), GENERIC_WRITE, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
      if (hout.IsInvalid) throw new IOException("No se pudo abrir el canal de comandos (" + Marshal.GetLastWin32Error() + ")");
      var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
      var buf = new byte[16];
      // La lectura se arranca antes de enviar para no perder la respuesta.
      var pending = fin.ReadAsync(buf, 0, 16);
      uint written;
      if (!WriteFile(hout, request, 16, out written, IntPtr.Zero))
        throw new IOException("No se pudo enviar el comando (" + Marshal.GetLastWin32Error() + ")");
      while (true) {
        var left = deadline - DateTime.UtcNow;
        if (left <= TimeSpan.Zero || !pending.Wait(left)) return null;
        if (pending.Result > 0 && buf[0] == 0x09) return buf;
        buf = new byte[16];
        pending = fin.ReadAsync(buf, 0, 16);
      }
    }
  }
}
'@
}

function Get-PercentEstimate([double]$millivolts) {
  # (mV, %): los tres del medio son los umbrales del LED del propio ratón.
  $curve = @(@(3500, 0), @(3680, 10), @(3760, 25), @(3890, 50), @(4000, 75), @(4200, 100))
  if ($millivolts -le $curve[0][0]) { return 0 }
  for ($i = 1; $i -lt $curve.Count; $i++) {
    $lo = $curve[$i - 1]; $hi = $curve[$i]
    if ($millivolts -le $hi[0]) {
      return [math]::Round($lo[1] + ($hi[1] - $lo[1]) * ($millivolts - $lo[0]) / ($hi[0] - $lo[0]))
    }
  }
  return 100
}

function Get-LedBand([double]$millivolts) {
  if ($millivolts -le 3680) { return '<= 10 % (el LED avisaria de bateria baja)' }
  if ($millivolts -le 3760) { return '<= 25 %' }
  if ($millivolts -le 3890) { return '26-50 %' }
  return '51-100 %'
}

# PID de los receptores DW según su firmware. Con cualquier otro PID, el canal es el del ratón por cable.
$script:ZowieReceiverPids = @('800A')

# Localiza el receptor o el ratón por cable (VID 04A5) con sus dos colecciones vendor: comandos 0xFF03
# y respuestas 0xFF04. Kind es 'Receptor' o 'Cable'. Si están los dos, se usa el ratón por cable:
# su tensión es la actual (el receptor ya no recibe nada) y además indica que está cargando.
function Find-ZowieDevice {
  $devices = @{}
  $collections = Get-PnpDevice -PresentOnly -Class HIDClass -ErrorAction SilentlyContinue |
    Where-Object { $_.InstanceId -match '^HID\\VID_04A5&PID_[0-9A-F]{4}&MI_\d{2}&COL\d{2}\\' }
  foreach ($c in $collections) {
    $caps = [ZowieHid]::GetCaps($c.InstanceId)
    if (-not $caps) { continue }
    $productId = $c.InstanceId.Substring(17, 4)   # HID\VID_04A5&PID_xxxx
    if (-not $devices.ContainsKey($productId)) {
      $kind = if ($script:ZowieReceiverPids -contains $productId) { 'Receptor' } else { 'Cable' }
      $devices[$productId] = [pscustomobject]@{ ProductId = $productId; Kind = $kind; OutId = $null; InId = $null }
    }
    if ($caps[0] -eq 0xFF03 -and $caps[2] -eq 16) { $devices[$productId].OutId = $c.InstanceId }
    if ($caps[0] -eq 0xFF04 -and $caps[1] -eq 16) { $devices[$productId].InId = $c.InstanceId }
  }
  $usable = @($devices.Values | Where-Object { $_.OutId -and $_.InId } | Sort-Object { $_.Kind -ne 'Cable' })
  if (-not $usable.Count) { return $null }
  return $usable[0]
}

# Envía el comando 0x15 y devuelve Status: Ok, NoReply, BadFormat o NoData (más los datos si es Ok).
# Charging: 'Cable' (ratón conectado por cable), 'Tension' (más de 4,25 V por radio) o $null.
function Read-ZowieBattery($device, [int]$TimeoutMs = 1500) {
  $reply = [ZowieHid]::ReadBatteryReport($device.OutId, $device.InId, $TimeoutMs)
  if (-not $reply) { return [pscustomobject]@{ Status = 'NoReply' } }
  $raw = ($reply | ForEach-Object { $_.ToString('X2') }) -join ' '
  if ($reply[1] -eq 0xE6 -or $reply[4] -ne 0xC8) { return [pscustomobject]@{ Status = 'BadFormat'; Raw = $raw } }
  $mv = ($reply[1] * 256 + $reply[2]) * 10
  if ($mv -eq 0) { return [pscustomobject]@{ Status = 'NoData'; Raw = $raw } }
  $charging = if ($device.Kind -eq 'Cable') { 'Cable' } elseif ($mv -ge 4250) { 'Tension' } else { $null }
  [pscustomobject]@{
    Status     = 'Ok'
    Raw        = $raw
    Millivolts = $mv
    Percent    = Get-PercentEstimate $mv
    LedBand    = Get-LedBand $mv
    Charging   = $charging
  }
}
