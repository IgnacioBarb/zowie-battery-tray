# Zowie Battery Tray

Muestra el **porcentaje de batería de los ratones inalámbricos ZOWIE de la serie DW** (EC2-DW, etc.) en la bandeja del sistema de Windows. ZOWIE no tiene ningún programa que lo enseñe: el ratón solo da una idea aproximada con su LED.

![Iconos: 88 %, 35 %, 14 %, 6 %, cargando y sin datos](docs/iconos.png)

*English: a small Windows tray app that shows the battery level of ZOWIE DW wireless mice (EC2-DW and others), which ZOWIE's own tools don't show. Download `ZowieBatteryTray.exe` from [Releases](../../releases) and run it.*

## Qué hace

- Enseña el porcentaje en un icono junto al reloj, con el color del nivel: verde (más del 50 %), amarillo (26-50 %), naranja (11-25 %) y rojo (10 % o menos, con un aviso para que lo cargues).
- Muestra un rayo mientras carga, tanto con el cable al PC como en la base del receptor 4K.
- Al pasar el ratón por encima indica la tensión de la batería y la hora de la última lectura.
- Se actualiza cada 5 minutos y al momento cuando conectas o desconectas el cable o el receptor. Con doble clic se actualiza al instante.
- Clic derecho: *Actualizar ahora*, *Iniciar con Windows* y *Salir*.

## Instalación

1. Descarga `ZowieBatteryTray.exe` de la página de [Releases](../../releases).
2. Ábrelo y pulsa **Sí** para instalarlo. Se copia en tu carpeta de programas, sin pedir permisos de administrador, aparece en el menú Inicio y se abre al iniciar Windows. Si pulsas **No**, se abre solo esa vez, sin instalar nada.
3. Para desinstalarlo: *Configuración > Aplicaciones > Aplicaciones instaladas > Zowie Battery Tray*.

Necesita Windows 10 u 11; .NET Framework 4.8 ya viene incluido. Como el `.exe` no está firmado, Windows puede mostrar "Windows protegió su PC": pulsa *Más información* y luego *Ejecutar de todas formas*.

## Cómo funciona

El receptor del ratón, y el propio ratón cuando está conectado por cable, tienen un canal HID privado. La aplicación solo envía un comando de lectura:

```
08 B4 15 00 00 00 00 00 00 00 00 00 00 00 00 00
```

Y la respuesta es `09 HH LL 00 C8 ...`, donde `HHLL` es la tensión de la batería en centésimas de voltio. Ese comando no escribe nada ni cambia ningún ajuste del ratón o del receptor.

- **Porcentaje:** es una estimación a partir de la tensión. Usa los umbrales del LED del propio ratón (3,68 V = 10 %, 3,76 V = 25 %, 3,89 V = 50 %), sacados de su firmware, y valores típicos de una batería de litio para el resto. Mientras carga sale más alto de lo real.
- **Carga por cable:** con el cable al PC el ratón aparece como su propio dispositivo USB y se carga.
- **Carga en la base:** por radio el ratón solo manda la tensión, así que la carga se deduce de cómo evoluciona: cargando se mantiene o sube, y al quitarlo de la base baja.

El protocolo se averiguó analizando la herramienta oficial de actualización de firmware de ZOWIE (*FW update tool for ZOWIE DW mouse*, v1.0.21.0), sin modificarla ni ejecutarla. Este repositorio no incluye nada de ZOWIE.

Probado con un **EC2-DW** y su receptor 4K. El resto de la serie DW (EC1/EC3-DW, FK2-DW, S2-DW, U2-DW, ZA13-DW) usa el mismo receptor, así que debería funcionar, pero no está probado.

## Compilarlo

No hace falta Visual Studio: el compilador de C# viene con Windows.

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

Deja el programa en `dist\ZowieBatteryTray.exe`. En [`powershell/`](powershell) hay además un script que hace una lectura y la muestra en la consola (`bateria_zowie.ps1`).

## Hecho con Claude

Este proyecto se hizo de principio a fin en una conversación con **Claude** (Anthropic), usando Claude Code. Eso incluye averiguar el protocolo analizando el firmware oficial, escribir la aplicación, el instalador y este README, y diseñar el icono.

## Aviso

Proyecto personal, sin relación con BenQ ni ZOWIE. ZOWIE es una marca de BenQ Corporation. Se ofrece tal cual, sin garantías (ver [LICENSE](LICENSE)).
