using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OmniHub.Core;

namespace OmniHub.UI;

public partial class InstallerWindow : Window
{
    int _page = 1;
    HardwareReport? _hw;
    readonly Dictionary<Component, CheckBox> _compChecks = new();
    CancellationTokenSource? _cts;
    bool _done, _installing;

    public InstallerWindow()
    {
        InitializeComponent();
        DirBox.Text = AppInfo.IsRegistered ? AppInfo.InstallDir : AppInfo.DefaultInstallDir;
        if (AppInfo.IsRegistered) NextBtn.Content = "Actualizar / reparar";
        ShowPage(1);
    }

    void Drag(object s, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }

    void ShowPage(int p)
    {
        _page = p;
        var pages = new UIElement[] { P1, P2, P3, P4, P5 };
        for (int i = 0; i < pages.Length; i++) pages[i].Visibility = i + 1 == p ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < Steps.Children.Count; i++)
        {
            var t = (TextBlock)Steps.Children[i];
            t.Foreground = i + 1 == p ? (Brush)FindResource("Accent") : i + 1 < p ? (Brush)FindResource("Text") : (Brush)FindResource("Muted");
            t.FontWeight = i + 1 == p ? FontWeights.SemiBold : FontWeights.Normal;
        }
        BackBtn.Visibility = p is > 1 and < 5 ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.Content = p switch { 1 => AppInfo.IsRegistered ? "Actualizar / reparar" : "Empezar", 4 => "Instalar", 5 => "Finalizar", _ => "Siguiente" };
        if (p == 2 && _hw == null) _ = ScanAsync();
    }

    async Task ScanAsync()
    {
        NextBtn.IsEnabled = false;
        ScanStatus.Text = "Escaneando placa base, CPU, gráfica, RAM, periféricos USB y audio…";
        _hw = await Task.Run(HardwareScan.Run);
        HwList.Children.Clear();
        AddHw("Placa base", _hw.Motherboard);
        AddHw("Procesador", _hw.Cpu);
        AddHw("Gráfica", _hw.Gpus.Count > 0 ? string.Join("\n", _hw.Gpus) : "—");
        AddHw("Memoria RAM", _hw.Ram.Count > 0 ? string.Join("\n", _hw.Ram.Distinct()) : "—");
        foreach (var g in _hw.Peripherals.GroupBy(p => p.Category).OrderBy(g => g.Key))
            AddHw(g.Key, string.Join("\n", g.Select(d => $"{d.Name}  ·  {d.Vendor}")));
        AddHw("Salidas de audio", _hw.AudioOutputs.Count > 0 ? string.Join("\n", _hw.AudioOutputs) : "—");
        if (_hw.VendorAppsRunning.Count > 0) AddHw("Apps del fabricante abiertas", string.Join("\n", _hw.VendorAppsRunning));
        ScanStatus.Text = "Esto es lo que he encontrado en este PC:";
        BuildComponents();
        VendorNote.Text = _hw.VendorAppsRunning.Count > 0 ? "Detectadas: " + string.Join(", ", _hw.VendorAppsRunning) : "";
        OptVendor.IsChecked = _hw.VendorAppsRunning.Count > 0;
        NextBtn.IsEnabled = true;
    }

    void AddHw(string title, string value)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = title, Foreground = (Brush)FindResource("Muted"), FontSize = 13 });
        var v = new TextBlock { Text = value, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Text") };
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        HwList.Children.Add(g);
    }

    void BuildComponents()
    {
        CompList.Children.Clear();
        _compChecks.Clear();
        foreach (var c in Components.All)
        {
            bool installed = c.IsInstalled();
            string why = c.Id switch
            {
                "pawnio" => $"Para tu placa ({_hw!.Motherboard}) y CPU: temperaturas y ventiladores.",
                "openrgb" => _hw!.HasRgbCandidates ? "Detectado hardware con posible RGB (placa/RAM/periféricos)." : "No veo RGB claro, pero sirve si añades dispositivos.",
                "eqapo" => _hw!.HasHeadset ? "Detectados auriculares/USB audio: ecualizador para que suenen bien." : "Ecualizador para tus altavoces o auriculares.",
                _ => ""
            };
            bool recommended = c.Id switch { "openrgb" => _hw!.HasRgbCandidates, _ => true };

            var card = new Border { Style = (Style)FindResource("CardBox"), Padding = new Thickness(16) };
            var sp = new StackPanel();
            var cb = new CheckBox
            {
                Content = new TextBlock { Text = c.Name + (installed ? "   ✓ ya instalado" : ""), FontWeight = FontWeights.SemiBold, FontSize = 14 },
                IsChecked = !installed && recommended, IsEnabled = !installed
            };
            _compChecks[c] = cb;
            sp.Children.Add(cb);
            sp.Children.Add(new TextBlock { Text = c.Purpose, Style = (Style)FindResource("Sub"), Margin = new Thickness(48, 4, 0, 0) });
            sp.Children.Add(new TextBlock { Text = "→ " + why, Foreground = (Brush)FindResource("Accent"), FontSize = 12, Margin = new Thickness(48, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
            card.Child = sp;
            CompList.Children.Add(card);
        }
        CompList.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("Sub"), FontSize = 12,
            Text = "Rendimiento, monitor de hardware y Spotify/multimedia no necesitan nada extra: van integrados en OmniHub."
        });
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Carpeta de instalación" };
        if (dlg.ShowDialog() == true) DirBox.Text = Path.Combine(dlg.FolderName, "OmniHub");
    }

    void Back_Click(object sender, RoutedEventArgs e) { if (_page > 1) ShowPage(_page - 1); }

    async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_page < 4) { ShowPage(_page + 1); return; }
        if (_page == 4) { ShowPage(5); await InstallAsync(); return; }
        if (_page == 5 && _done)
        {
            if (LaunchAfter.IsChecked == true)
                try { Process.Start(new ProcessStartInfo(Path.Combine(DirBox.Text, "OmniHub.exe")) { UseShellExecute = true }); } catch { }
            Application.Current.Shutdown();
        }
    }

    async Task InstallAsync()
    {
        _installing = true;
        NextBtn.IsEnabled = false;
        _cts = new CancellationTokenSource();
        var log = new Progress<string>(s => { Log.Text += s + "\n"; LogScroll.ScrollToEnd(); });
        var prog = new Progress<double>(v => SubBar.Value = v);
        var o = new InstallOptions
        {
            Dir = DirBox.Text.Trim(),
            DesktopShortcut = OptDesktop.IsChecked == true,
            StartWithWindows = OptStartup.IsChecked == true,
            CloseVendorApps = OptVendor.IsChecked == true,
            Components = _compChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList(),
        };
        var total = new Progress<double>(v => Bar.Value = v);
        try
        {
            Bar.Value = 0.05;
            await Installer.InstallAsync(o, log, new Progress<double>(v => { SubBar.Value = v; }), _cts.Token);
            Bar.Value = 1;
            InstallTitle.Text = "¡Listo! OmniHub está instalado";
            ((IProgress<string>)log).Report("\nPuedes abrirlo desde el escritorio o el menú Inicio. Para desinstalar: Configuración → Aplicaciones → OmniHub.");
            _done = true;
            LaunchAfter.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            InstallTitle.Text = "Instalación cancelada";
        }
        catch (Exception ex)
        {
            InstallTitle.Text = "Error en la instalación";
            ((IProgress<string>)log).Report("ERROR: " + ex.Message);
            _done = true;
        }
        _installing = false;
        NextBtn.IsEnabled = true;
        NextBtn.Content = "Finalizar";
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_installing)
        {
            if (MessageBox.Show("¿Cancelar la instalación?", "OmniHub", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _cts?.Cancel();
            return;
        }
        Application.Current.Shutdown();
    }
}
