<p align="center"><img src="Assets/logo.png" width="110" alt="OmniHub"></p>

<h1 align="center">OmniHub</h1>
<p align="center"><b>Un centro de control universal para tu PC</b>: luces RGB, ventiladores, rendimiento y sonido en una sola app, con cualquier marca de hardware.</p>

<p align="center"><a href="https://github.com/millachorda/OmniHub/releases/latest"><b>⬇ Descargar OmniHub-Setup.exe</b></a></p>

---

## Qué hace

| Módulo | Qué controla | Con qué hardware |
|---|---|---|
| **Iluminación** | Estático, respirar, arcoíris, ola, ciclo, temperatura de la CPU, color de la carátula de la música | Placas MSI/ASUS/Gigabyte/ASRock, RAM, GPU, ratones, teclados, auriculares, hubs… (todo lo que soporta [OpenRGB](https://openrgb.org)) |
| **Ventiladores** | Modos Inteligente / Rendimiento / Silencioso / Personalizado (curva editable), ventilador cero en la GPU, Turbo y control por canal | Cualquier placa base y gráfica que soporte [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) + [PawnIO](https://pawnio.eu) |
| **Rendimiento** | Escenarios Máximo rendimiento / Equilibrado / Silencioso (plan de energía + ventiladores) y Modo juego de Windows | Cualquier PC con Windows 10/11 |
| **Monitor** | Todos los sensores en vivo: temperaturas, frecuencias, voltajes, consumo, uso y memoria | Lo que soporte LibreHardwareMonitor |
| **Audio** | Dispositivo por defecto, volumen, micro, corrección [AutoEq](https://autoeq.app) automática, audio espacial, efectos y ecualizador de 10 bandas | Cualquier auricular o altavoz (motor: [Equalizer APO](https://sourceforge.net/projects/equalizerapo/)) |
| **Integraciones** | Spotify y cualquier reproductor: carátula, controles y luces con el color de la carátula | — |

## Instalación

1. Descarga **OmniHub-Setup.exe** de la [última release](https://github.com/millachorda/OmniHub/releases/latest) y ábrelo.
2. El instalador **escanea tu hardware** (placa, CPU, GPU, RAM, periféricos, audio) y te recomienda solo los componentes que tu PC necesita.
3. Descarga la versión completa de OmniHub y sus módulos **desde las releases de este repositorio** y los instala en segundo plano, sin ventanas.
4. Crea accesos directos, el launcher y la entrada para desinstalar en *Configuración → Aplicaciones*. Al desinstalar se quitan también los módulos (sensores, iluminación y audio).

El launcher comprueba al abrirse si hay una versión nueva en GitHub y se actualiza solo.

> OmniHub pide permisos de administrador: los necesita para los ventiladores, el ecualizador y para instalarse en *Archivos de programa*.

## Compilar

Requiere el SDK de .NET 8 en Windows.

```bash
dotnet publish OmniHub.csproj -c Release -o dist
```

Sale un único `dist/OmniHub.exe` autocontenido (instalador + launcher + app). Al crear una etiqueta `vX.Y.Z`, GitHub Actions lo compila y lo sube a Releases.

Los módulos se basan en proyectos abiertos y se redistribuyen sin modificar: ver [THIRD_PARTY.md](THIRD_PARTY.md).

## Avisos

- Controlar ventiladores y RGB por software actúa sobre el hardware. Al cerrar OmniHub, los ventiladores vuelven al control de la BIOS.
- Si una app del fabricante (G HUB, MSI Center, iCUE, Synapse…) sigue abierta, puede pisar los cambios. OmniHub puede cerrarlas por ti.
- OmniHub no está afiliado a ningún fabricante. Los nombres de productos pertenecen a sus dueños.
