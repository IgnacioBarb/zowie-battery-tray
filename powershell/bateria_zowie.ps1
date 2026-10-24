<#
.SYNOPSIS
  Lee la batería de un ratón Zowie de la serie DW (EC2-DW, etc.) a través de su receptor.

.DESCRIPTION
  Envía al receptor solo el comando de lectura 08 B4 15 (detalles del protocolo en zowie_comun.ps1)
  y muestra la tensión, el porcentaje estimado y el tramo que mostraría el LED del ratón.
#>
param([int]$TimeoutMs = 1500)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'zowie_comun.ps1')

$device = Find-ZowieDevice
if (-not $device) {
  Write-Host 'No encuentro ni el receptor Zowie DW ni el ratón conectado por cable.'
  exit 1
}
$source = if ($device.Kind -eq 'Cable') { 'ratón por cable' } else { 'receptor' }
Write-Host "Conectado:              $source (PID $($device.ProductId))"

$battery = Read-ZowieBattery $device $TimeoutMs
if ($battery.Raw) { Write-Host "Respuesta:              $($battery.Raw)" }
switch ($battery.Status) {
  'NoReply' {
    Write-Host 'El receptor no respondió a tiempo.'
    exit 2
  }
  'BadFormat' {
    Write-Host 'La respuesta no tiene el formato esperado; no la interpreto.'
    exit 3
  }
  'NoData' {
    Write-Host 'El receptor todavía no tiene la tensión del ratón (¿apagado o sin emparejar?). Mueve el ratón y repite.'
    exit 4
  }
}
Write-Host ('Tensión de la batería:  {0:N2} V' -f ($battery.Millivolts / 1000))
Write-Host ('Batería estimada:       {0} %' -f $battery.Percent)
Write-Host ('Según el LED del ratón: {0}' -f $battery.LedBand)
switch ($battery.Charging) {
  'Cable' { Write-Host 'Carga:                  cargando por cable (el porcentaje sale más alto mientras carga)' }
  'Tension' { Write-Host 'Carga:                  probablemente cargando (más de 4,25 V; recién sacado de la base también lo da)' }
  default { Write-Host 'Carga:                  sin carga detectada' }
}
