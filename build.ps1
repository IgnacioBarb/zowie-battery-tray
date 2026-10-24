# Compila dist\ZowieBatteryTray.exe con el compilador de C# que trae Windows (.NET Framework 4.8):
# no hace falta instalar Visual Studio ni el SDK de .NET.
#   powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "No encuentro el compilador de .NET Framework 4: $csc" }

$icon = Join-Path $root 'assets\app.ico'
if (-not (Test-Path $icon)) { & (Join-Path $root 'tools\make-icon.ps1') }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$exe = Join-Path $dist 'ZowieBatteryTray.exe'
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object FullName

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
  "/out:$exe" "/win32icon:$icon" "/win32manifest:$(Join-Path $root 'src\app.manifest')" `
  /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
  $sources
if ($LASTEXITCODE) { throw "La compilación ha fallado ($LASTEXITCODE)" }
$item = Get-Item $exe
"Compilado: $($item.FullName) ($([math]::Round($item.Length / 1KB)) KB)"
