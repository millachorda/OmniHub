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
        foreach (var d in Logitech) d.Dispose();
    }
}
