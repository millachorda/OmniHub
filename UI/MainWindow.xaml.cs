using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OmniHub.Core;
using OmniHub.Services;

namespace OmniHub.UI;

public partial class MainWindow : Window
{
    readonly List<Action> _live = new();
    readonly List<Action> _cleanup = new();   // desuscripciones al cambiar de página
    System.Windows.Forms.NotifyIcon? _tray;
    bool _reallyClose;
    string _current = "Panel";

    public MainWindow()
    {
        InitializeComponent();
        Hub.ShowMain = ShowFromTray;
        Hub.ToastHandler = t => Dispatcher.BeginInvoke(() => Toast(t));
        Hub.Ticked += OnTick;
        SetupTray();
        Loaded += async (_, _) =>
        {
            Page.Content = Center("Iniciando servicios…");
            await Hub.StartAsync(new Progress<string>(s => Page.Content = Center(s)));
            Navigate("Panel");
        };
    }

    public void StartHidden()
    {
        // Arranque con Windows: servicios en marcha, ventana oculta en la bandeja
        WindowState = WindowState.Minimized;
        ShowInTaskbar = false;
        Show();
        Hide();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
    }

    void OnTick()
    {
        ProfileLabel.Text = "Perfil: " + (Hub.ActiveProfile.Length > 0 ? Hub.ActiveProfile + (Hub.AutoProfileReason.Length > 0 ? $" ({Hub.AutoProfileReason})" : "") : "—");
        ClockLabel.Text = $"CPU {Hub.Fans.CpuTemp:0}°C · GPU {Hub.Fans.GpuTemp:0}°C";
        if (!IsVisible) return;
        foreach (var a in _live.ToArray()) try { a(); } catch { }
    }

    // ---------- Navegación ----------
    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && IsLoaded) Navigate(tag);
    }

    void Navigate(string tag)
    {
        _current = tag;
        _live.Clear();
        foreach (var c in _cleanup) try { c(); } catch { }
        _cleanup.Clear();
        try
        {
            Page.Content = tag switch
            {
                "Panel" => PageDashboard(),
                "Luces" => PageLights(),
                "Fans" => PageFans(),
                "Rendimiento" => PagePerformance(),
                "Monitor" => PageMonitor(),
                "Mouse" => PageMouse(),
                "Audio" => PageAudio(),
                "Apps" => PageIntegrations(),
                "Perfiles" => PageProfiles(),
                "Ajustes" => PageSettings(),
                _ => Center("")
            };
        }
        catch (Exception ex) { Page.Content = Center("Error mostrando la página: " + ex.Message); }
        foreach (var a in _live.ToArray()) try { a(); } catch { }
    }

    void Refresh() => Navigate(_current);

    // ---------- Ventana ----------
    void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object s, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // Evita que la ventana maximizada sin bordes se salga de la pantalla
        RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_reallyClose && Settings.Current.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            _tray?.ShowBalloonTip(2000, "OmniHub", "Sigue funcionando en la bandeja (luces, ventiladores y perfiles activos).", System.Windows.Forms.ToolTipIcon.None);
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Application.Current.Shutdown();
        base.OnClosed(e);
    }

    public void ExitApp()
    {
        _reallyClose = true;
        Close();
    }

    void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    void SetupTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "OmniHub",
            Visible = true,
        };
        try { _tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(AppInfo.ExePath); } catch { }
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir OmniHub", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        var prof = new System.Windows.Forms.ToolStripMenuItem("Perfiles");
        menu.Items.Add(prof);
        menu.Opening += (_, _) =>
        {
            prof.DropDownItems.Clear();
            foreach (var p in Settings.Current.Profiles)
            {
                var pp = p;
                prof.DropDownItems.Add(p.Name, null, (_, _) => Dispatcher.Invoke(() => Hub.ApplyProfile(pp)));
            }
        };
        menu.Items.Add("Silenciar / activar micrófono", null, (_, _) => Dispatcher.Invoke(() => Actions.Run("Silenciar micrófono (sistema)", true)));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Dispatcher.Invoke(ExitApp));
        _tray.ContextMenuStrip = menu;
    }

    void Toast(string text)
    {
        ToastText.Text = text;
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2200))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2600))));
        ToastBox.BeginAnimation(OpacityProperty, a);
        if (!IsVisible) _tray?.ShowBalloonTip(1500, "OmniHub", text, System.Windows.Forms.ToolTipIcon.None);
    }

    // ---------- Constructores de UI ----------
    Brush B(string k) => (Brush)FindResource(k);
    Style S(string k) => (Style)FindResource(k);

    UIElement Center(string text) => new TextBlock { Text = text, Style = S("Sub"), FontSize = 15, Margin = new Thickness(0, 120, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };

    TextBlock H1(string t) => new() { Text = t, Style = S("H1") };
    TextBlock H2(string t) => new() { Text = t, Style = S("H2") };
    TextBlock Sub(string t, double size = 13) => new() { Text = t, Style = S("Sub"), FontSize = size };
    TextBlock Txt(string t, double size = 13) => new() { Text = t, FontSize = size, Foreground = B("Text"), TextWrapping = TextWrapping.Wrap };

    StackPanel Header(string title, string subtitle)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 20) };
        sp.Children.Add(H1(title));
        sp.Children.Add(Sub(subtitle, 14));
        return sp;
    }

    Border Card(params UIElement[] kids)
    {
        var sp = new StackPanel();
        foreach (var k in kids) sp.Children.Add(k);
        return new Border { Style = S("CardBox"), Child = sp };
    }

    static StackPanel Stack(params UIElement[] kids)
    {
        var sp = new StackPanel();
        foreach (var k in kids) sp.Children.Add(k);
        return sp;
    }

    static WrapPanel Row(params UIElement[] kids)
    {
        var wp = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var k in kids)
        {
            if (k is TextBlock tb) { tb.VerticalAlignment = VerticalAlignment.Center; tb.Margin = new Thickness(0, 0, 8, 8); }
            wp.Children.Add(k);
        }
        return wp;
    }

    static UniformGrid Cols(int n, params UIElement[] kids)
    {
        var g = new UniformGrid { Columns = n, Margin = new Thickness(-7, 0, -7, 0) };
        foreach (var k in kids)
        {
            if (k is FrameworkElement fe) fe.Margin = new Thickness(7, 0, 7, 14);
            g.Children.Add(k);
        }
        return g;
    }

    Button Btn(string text, Action click, bool accent = false)
    {
        var b = new Button { Content = text };
        if (accent) b.Style = S("AccentButton");
        b.Click += (_, _) => click();
        return b;
    }

    CheckBox Toggle(string text, bool value, Action<bool> changed)
    {
        var c = new CheckBox { Content = text, IsChecked = value };
        c.Checked += (_, _) => changed(true);
        c.Unchecked += (_, _) => changed(false);
        return c;
    }

    ComboBox Combo(IEnumerable<string> items, string? selected, Action<string> changed, double width = 220)
    {
        var c = new ComboBox { Width = width, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var i in items) c.Items.Add(i);
        c.SelectedItem = selected != null && c.Items.Contains(selected) ? selected : (c.Items.Count > 0 ? c.Items[0] : null);
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is string s) changed(s); };
        return c;
    }

    UIElement LabeledSlider(string label, double min, double max, double value, Action<double> changed, string fmt = "0", string unit = "", double tick = 1)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        var l = Sub(label); l.VerticalAlignment = VerticalAlignment.Center;
        var v = Txt(value.ToString(fmt) + unit); v.VerticalAlignment = VerticalAlignment.Center; v.TextAlignment = TextAlignment.Right;
        var s = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = tick, IsSnapToTickEnabled = tick > 0, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += (_, e) => { v.Text = e.NewValue.ToString(fmt) + unit; changed(e.NewValue); };
        Grid.SetColumn(s, 1); Grid.SetColumn(v, 2);
        g.Children.Add(l); g.Children.Add(s); g.Children.Add(v);
        return g;
    }

    Border Stat(string label, Func<string> value, string? accent = null)
    {
        var big = new TextBlock { Style = S("Big") };
        if (accent != null) big.Foreground = B(accent);
        _live.Add(() => big.Text = value());
        var b = Card(Sub(label), big);
        b.Padding = new Thickness(18, 14, 18, 14);
        return b;
    }

    Border Banner(string text, string btnText, Action click)
    {
        var dp = new DockPanel();
        var btn = Btn(btnText, click, true);
        btn.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(btn, Dock.Right);
        dp.Children.Add(btn);
        var t = Txt(text); t.VerticalAlignment = VerticalAlignment.Center;
        dp.Children.Add(t);
        return new Border { Style = S("CardBox"), BorderBrush = B("Warn"), Child = dp };
    }

    /// <summary>Selector de color: muestras rápidas + hex + tono.</summary>
    UIElement ColorPicker(string label, string current, Action<string> changed)
    {
        string[] swatches = { "#FF0000", "#FF5A00", "#FFB000", "#FFFF00", "#7CFF00", "#00FF40", "#00FFC0", "#00E5FF", "#0080FF", "#2020FF", "#8C52FF", "#FF00FF", "#FF2080", "#FFFFFF" };
        var preview = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(8), BorderBrush = B("Line"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 8) };
        var hex = new TextBox { Width = 100, Text = current };
        void Set(string h, bool fromBox = false)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(h);
                preview.Background = new SolidColorBrush(c);
                if (!fromBox) hex.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                changed($"#{c.R:X2}{c.G:X2}{c.B:X2}");
            }
            catch { }
        }
        try { preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(current)); } catch { }
        hex.TextChanged += (_, _) => { if (hex.Text.Length == 7) Set(hex.Text, true); };
        var row = Row(preview, hex);
        foreach (var sw in swatches)
        {
            var b = new Button
            {
                Width = 26, Height = 26, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 8),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sw)), BorderBrush = B("Line"), ToolTip = sw
            };
            var s = sw;
            b.Click += (_, _) => Set(s);
            row.Children.Add(b);
        }
        var hue = new Slider { Minimum = 0, Maximum = 359, Margin = new Thickness(0, 0, 0, 4) };
        hue.Background = Brushes.Transparent;
        hue.ValueChanged += (_, e) =>
        {
            var (r, g, bb) = RgbService.Hsv(e.NewValue, 1, 1);
            Set($"#{(int)r:X2}{(int)g:X2}{(int)bb:X2}");
        };
        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i <= 6; i++)
        {
            var (r, g, bb) = RgbService.Hsv(i * 59.8, 1, 1);
            rainbow.GradientStops.Add(new GradientStop(Color.FromRgb((byte)r, (byte)g, (byte)bb), i / 6.0));
        }
        var hueBar = new Grid();
        hueBar.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = rainbow, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });
        hueBar.Children.Add(hue);
        return Stack(Sub(label), row, hueBar);
    }
}
