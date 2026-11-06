using System.Diagnostics;
using System.Runtime.InteropServices;
using OmniHub.Core;

namespace OmniHub.Services;

/// <summary>
/// Ratón universal: ajustes de Windows (cualquier ratón), reasignación de botones por software
/// (cualquier ratón) y DPI/tasa por hardware para Logitech vía HID++.
/// </summary>
public sealed class MouseService : IDisposable
{
    public static readonly string[] Buttons = { "Botón central", "Lateral trasero (Atrás)", "Lateral delantero (Adelante)" };

    public List<HidppDevice> Logitech { get; private set; } = new();
    public HidppDevice? Active => Logitech.FirstOrDefault();
    int _dpiBeforeSniper;
    int _stage;

    public void Discover()
    {
        foreach (var d in Logitech) d.Dispose();
        try { Logitech = HidppDevice.Discover(); } catch { Logitech = new(); }
        SetupGButtons();
    }

    // ---------- Botones G de Logitech (sin G HUB) ----------
    /// <summary>Se dispara con el número de botón (1..n) al pulsarlo: sirve para "aprender" cuál es cuál.</summary>
    public event Action<int>? GButtonPressed;
    public int GButtonCount { get; private set; }
    int _mask;

    /// <summary>Clave de ajustes del botón n; la capa G-Shift lleva "^" al final.</summary>
    public static string GKey(int n, bool shift = false) => shift ? $"LG{n}^" : $"LG{n}";
    static string GAction(int n, bool shift = false)
    {
        var m = Settings.Current.ButtonMap;
        if (shift && m.TryGetValue(GKey(n, true), out var s) && s != Actions.Default) return s;
        return m.TryGetValue(GKey(n), out var a) ? a : Actions.Default;
    }

    /// <summary>Capa G-Shift activa mientras se mantiene el botón que tiene la acción «G-Shift».</summary>
    public bool GShift { get; private set; }
    int _shiftButton;
    readonly Dictionary<int, string> _downAction = new(); // la acción que se ejecutó al pulsar (para soltar la misma)

    void SetupGButtons()
    {
        GButtonCount = 0;
        if (Active is not { } d || !d.HasButtonSpy) return;
        GButtonCount = Math.Min(16, d.ButtonCount);
        if (Hub.TestMode) return;
        d.ButtonsChanged += OnButtons;
        d.StartSpy();
        ApplyRemap();
    }

    /// <summary>
    /// Los botones con acción de OmniHub se desactivan en el ratón (los ejecutamos nosotros);
    /// los laterales sin asignar se quedan como Atrás/Adelante nativos.
    /// </summary>
    public void ApplyRemap()
    {
        if (Active is not { } d || GButtonCount == 0) return;
        var map = new byte[16];
        for (int i = 1; i <= GButtonCount; i++)
        {
            bool free = GAction(i) == Actions.Default && !(Settings.Current.ButtonMap.TryGetValue(GKey(i, true), out var s) && s != Actions.Default);
            map[i - 1] = i <= 3 ? (byte)i : free && i is 4 or 5 ? (byte)i : (byte)0;
        }
        Task.Run(() => d.SetRemap(map));
    }

    void OnButtons(int mask)
    {
        int changed = mask ^ _mask;
        _mask = mask;
        for (int i = 0; i < 16; i++)
        {
            if (((changed >> i) & 1) == 0) continue;
            int btn = i + 1;
            bool down = ((mask >> i) & 1) != 0;
            if (down) GButtonPressed?.Invoke(btn);
            if (btn <= 3) continue;
            string a;
            if (down) { a = GAction(btn, GShift && btn != _shiftButton); _downAction[btn] = a; }
            else a = _downAction.TryGetValue(btn, out var d) ? d : GAction(btn);
            if (a == Actions.GShift)
            {
                GShift = down; _shiftButton = down ? btn : 0;
                continue;
            }
            if (a == Actions.Default) continue;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Actions.Run(a, down));
        }
    }

    // ---------- Ajustes de Windows ----------
    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint u, IntPtr p, uint f);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint u, int[] p, uint f);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint u, ref int p, uint f);
    const uint SPIF = 0x01 | 0x02;

    public static int PointerSpeed
    {
        get { int v = 10; SystemParametersInfo(0x0070, 0, ref v, 0); return v; }
        set => SystemParametersInfo(0x0071, 0, (IntPtr)Math.Clamp(value, 1, 20), SPIF);
    }

    public static bool Acceleration
    {
        get { var a = new int[3]; SystemParametersInfo(0x0003, 0, a, 0); return a[2] != 0; }
        set => SystemParametersInfo(0x0004, 0, value ? new[] { 6, 10, 1 } : new[] { 0, 0, 0 }, SPIF);
    }

    public static int ScrollLines
    {
        get { int v = 3; SystemParametersInfo(0x0068, 0, ref v, 0); return v; }
        set => SystemParametersInfo(0x0069, (uint)Math.Clamp(value, 1, 100), IntPtr.Zero, SPIF);
    }

    // ---------- DPI ----------
    public void SetDpi(int dpi) { if (Active is { } d) Task.Run(() => d.SetDpi(dpi)); }

    public void CycleDpi()
    {
        var st = Settings.Current.DpiStages;
        if (st.Count == 0 || Active == null) return;
        _stage = (_stage + 1) % st.Count;
        SetDpi(st[_stage]);
        Hub.Toast($"DPI: {st[_stage]}");
    }

    public void StepDpi(int dir)
    {
        var st = Settings.Current.DpiStages;
        if (st.Count == 0 || Active == null) return;
        _stage = Math.Clamp(_stage + dir, 0, st.Count - 1);
        SetDpi(st[_stage]);
        Hub.Toast($"DPI: {st[_stage]}");
    }

    public void SniperDown()
    {
        if (Active is not { } d) return;
        Task.Run(() => { _dpiBeforeSniper = d.GetDpi(); d.SetDpi(Settings.Current.SniperDpi); });
    }

    public void SniperUp()
    {
        if (Active is not { } d) return;
        Task.Run(() => { if (_dpiBeforeSniper > 0) d.SetDpi(_dpiBeforeSniper); });
    }

    // ---------- Gancho global de botones ----------
    delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? n);

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT { public int x, y; public uint mouseData, flags, time; public IntPtr extra; }

    IntPtr _hook;
    HookProc? _proc;

    public void InstallHook()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = Hook;
        _hook = SetWindowsHookEx(14, _proc, GetModuleHandle(null), 0);
    }

    IntPtr Hook(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(l);
            bool injected = (info.flags & 1) != 0;
            int msg = (int)w;
            string? button = null; bool down = false;
            switch (msg)
            {
                case 0x0207: button = Buttons[0]; down = true; break;
                case 0x0208: button = Buttons[0]; break;
                case 0x020B: case 0x020C:
                    var x = (info.mouseData >> 16) & 0xFFFF;
                    button = x == 1 ? Buttons[1] : Buttons[2];
                    down = msg == 0x020B;
                    break;
            }
            if (button != null && !injected && Settings.Current.ButtonMap.TryGetValue(button, out var action) && action != Actions.Default)
            {
                // Ejecutar fuera del gancho para no bloquear el ratón
                var a = action; var dn = down;
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Actions.Run(a, dn));
                return (IntPtr)1; // tragamos el clic original
            }
        }
        return CallNextHookEx(_hook, code, w, l);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        // Al salir el ratón vuelve a funcionar solo: laterales como Atrás/Adelante
        if (Active is { } m && GButtonCount > 0 && !Hub.TestMode)
        {
            try { m.StopSpy(); m.SetRemap(new byte[] { 1, 2, 3, 4, 5 }); } catch { }
        }
        foreach (var d in Logitech) d.Dispose();
    }
}
