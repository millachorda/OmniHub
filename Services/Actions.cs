using System.Diagnostics;
using System.Runtime.InteropServices;
using OmniHub.Core;

namespace OmniHub.Services;

/// <summary>Acciones asignables a botones del ratón (y usadas por las integraciones).</summary>
public static class Actions
{
    public const string Default = "Predeterminado";

    public static readonly string[] All =
    {
        Default, "Desactivado",
        "Francotirador DPI (mantener)", "Ciclar DPI", "Turbo ventiladores (activar/parar)",
        "Reproducir / Pausa", "Siguiente pista", "Pista anterior",
        "Subir volumen", "Bajar volumen", "Silenciar sonido",
        "Silenciar micrófono (sistema)", "Discord: silenciar micro",
        "OBS: grabar / parar", "OBS: emitir / parar", "OBS: guardar repetición",
        "Copiar", "Pegar", "Deshacer", "Atrás", "Adelante",
        "Mostrar escritorio", "Captura de pantalla", "Administrador de tareas",
        "Abrir OmniHub",
    };

    // Teclas virtuales
    const byte CTRL = 0x11, SHIFT = 0x10, ALT = 0x12, WIN = 0x5B, ESC = 0x1B;

    public static void Run(string action, bool down)
    {
        // Acciones con "mantener" reaccionan a pulsar y soltar; el resto solo a pulsar
        if (action == "Francotirador DPI (mantener)")
        {
            if (down) Hub.Mouse.SniperDown(); else Hub.Mouse.SniperUp();
            return;
        }
        if (!down) return;
        switch (action)
        {
            case "Desactivado": break;
            case "Ciclar DPI": Hub.Mouse.CycleDpi(); break;
            case "Turbo ventiladores (activar/parar)":
                Hub.Fans.Boost = !Hub.Fans.Boost;
                Hub.Toast(Hub.Fans.Boost ? "Turbo: ventiladores al 100 %" : "Turbo desactivado");
                break;
            case "Reproducir / Pausa": Key(0xB3); break;
            case "Siguiente pista": Key(0xB0); break;
            case "Pista anterior": Key(0xB1); break;
            case "Subir volumen": Key(0xAF); break;
            case "Bajar volumen": Key(0xAE); break;
            case "Silenciar sonido": Key(0xAD); break;
            case "Silenciar micrófono (sistema)":
                var muted = Hub.Audio.ToggleMicMute();
                Hub.Toast(muted ? "Micrófono silenciado" : "Micrófono activo");
                break;
            case "Discord: silenciar micro": Hotkey(Settings.Current.DiscordMuteHotkey); break;
            case "OBS: grabar / parar": _ = Hub.Obs.Request("ToggleRecord"); break;
            case "OBS: emitir / parar": _ = Hub.Obs.Request("ToggleStream"); break;
            case "OBS: guardar repetición": _ = Hub.Obs.Request("SaveReplayBuffer"); break;
            case "Copiar": Combo(CTRL, (byte)'C'); break;
            case "Pegar": Combo(CTRL, (byte)'V'); break;
            case "Deshacer": Combo(CTRL, (byte)'Z'); break;
            case "Atrás": Key(0xA6); break;
            case "Adelante": Key(0xA7); break;
            case "Mostrar escritorio": Combo(WIN, (byte)'D'); break;
            case "Captura de pantalla": Combo(WIN, SHIFT, (byte)'S'); break;
            case "Administrador de tareas": Combo(CTRL, SHIFT, ESC); break;
            case "Abrir OmniHub": Hub.ShowMain?.Invoke(); break;
        }
    }

    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    static void Key(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 2, UIntPtr.Zero);
    }

    static void Combo(params byte[] keys)
    {
        foreach (var k in keys) keybd_event(k, 0, 0, UIntPtr.Zero);
        foreach (var k in keys.Reverse()) keybd_event(k, 0, 2, UIntPtr.Zero);
    }

    /// <summary>Pulsa un atajo escrito como "Ctrl+Shift+M".</summary>
    public static void Hotkey(string text)
    {
        var keys = new List<byte>();
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = raw.ToUpperInvariant();
            byte? vk = p switch
            {
                "CTRL" or "CONTROL" => CTRL, "SHIFT" or "MAYUS" or "MAYÚS" => SHIFT, "ALT" => ALT, "WIN" => WIN,
                _ when p.Length == 1 => (byte)p[0],
                _ when p.StartsWith('F') && int.TryParse(p[1..], out var f) && f is >= 1 and <= 24 => (byte)(0x6F + f),
                _ => null
            };
            if (vk != null) keys.Add(vk.Value);
        }
        if (keys.Count > 0) Combo(keys.ToArray());
    }
}
