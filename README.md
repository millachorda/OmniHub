<p align="center"><img src="Assets/logo.png" width="110" alt="OmniHub"></p>

<h1 align="center">OmniHub</h1>
<p align="center"><b>Un centro de control universal para tu PC</b>: luces RGB, ventiladores, ratón, sonido e integraciones en una sola app, con cualquier marca de hardware.</p>

<p align="center"><a href="https://github.com/millachorda/OmniHub/releases/latest"><b>⬇ Descargar OmniHub-Setup.exe</b></a></p>

---

## Qué hace

| Módulo | Qué controla | Con qué hardware |
|---|---|---|
| **Iluminación** | Estático, respirar, arcoíris, ola, ciclo, temperatura de la CPU, color de la carátula de la música | Placas MSI/ASUS/Gigabyte/ASRock, RAM, GPU, ratones, teclados, auriculares, hubs… (todo lo que soporta [OpenRGB](https://openrgb.org)) |
| **Ventiladores** | Modos Inteligente / Rendimiento / Silencioso / Personalizado (curva editable), ventilador cero en la GPU, Turbo y control por canal | Cualquier placa base y gráfica que soporte [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) + [PawnIO](https://pawnio.eu) |
| **Ratón** | Velocidad, aceleración, rueda y reasignación de botones (cualquier ratón); DPI, francotirador, niveles, tasa de sondeo y batería por hardware | Cualquier ratón · Logitech por HID++ sin G HUB |
| **Audio** | Dispositivo por defecto, volumen, micro, ecualizador de 10 bandas con presets y perfiles [AutoEq](https://autoeq.app) | Cualquier auricular o altavoz (motor: [Equalizer APO](https://sourceforge.net/projects/equalizerapo/)) |
| **Integraciones** | Spotify y cualquier reproductor, OBS (grabar, emitir, escenas, luces rojas al grabar), Discord (Rich Presence, silenciar), lanzador de apps y **perfiles automáticos por app** | — |

## Instalación

1. Descarga **OmniHub-Setup.exe** de la [última release](https://github.com/millachorda/OmniHub/releases/latest) y ábrelo.
2. El instalador **escanea tu hardware** (placa, CPU, GPU, RAM, periféricos, audio) y te recomienda solo los componentes que tu PC necesita.
3. Descarga la versión completa de OmniHub **desde las releases de este repositorio** y los componentes desde sus webs oficiales.
4. Crea accesos directos, el launcher y la entrada para desinstalar en *Configuración → Aplicaciones*.

El launcher comprueba al abrirse si hay una versión nueva en GitHub y se actualiza solo.

> OmniHub pide permisos de administrador: los necesita para los ventiladores, el ecualizador y para instalarse en *Archivos de programa*.

## Compilar

Requiere el SDK de .NET 8 en Windows.

```bash
dotnet publish OmniHub.csproj -c Release -o dist
```

Sale un único `dist/OmniHub.exe` autocontenido (instalador + launcher + app). Al crear una etiqueta `vX.Y.Z`, GitHub Actions lo compila y lo sube a Releases.

## Avisos

- Controlar ventiladores y RGB por software actúa sobre el hardware. Al cerrar OmniHub, los ventiladores vuelven al control de la BIOS.
- Si una app del fabricante (G HUB, MSI Center, iCUE, Synapse…) sigue abierta, puede pisar los cambios. OmniHub puede cerrarlas por ti.
- OmniHub no está afiliado a ningún fabricante. Los nombres de productos pertenecen a sus dueños.
