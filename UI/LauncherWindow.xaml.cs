using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OmniHub.Core;
using OmniHub.Services;

namespace OmniHub.UI;

public partial class LauncherWindow : Window
{
    public LauncherWindow()
    {
        InitializeComponent();
        Ver.Text = $"v{AppInfo.Version} · Centro de control universal";
        Skip.IsChecked = Settings.Current.SkipLauncher;
        Loaded += async (_, _) => await CheckAsync();
        Spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(6)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    void Drag(object s, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) try { DragMove(); } catch { } }

    void Row(string name, bool? ok, string detail)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = new TextBlock
        {
            Text = ok == true ? "●" : ok == false ? "●" : "○",
            Foreground = (Brush)FindResource(ok == true ? "Ok" : ok == false ? "Warn" : "Muted"),
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center
        };
        var t = new TextBlock { Text = name, FontSize = 13 };
        var d = new TextBlock { Text = detail, FontSize = 12, Foreground = (Brush)FindResource("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 };
        Grid.SetColumn(t, 1); Grid.SetColumn(d, 2);
        g.Children.Add(dot); g.Children.Add(t); g.Children.Add(d);
        Checks.Children.Add(g);
    }

    async Task CheckAsync()
    {
        Checks.Children.Clear();
        Status.Text = "Comprobando sistema…";
        bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        Row("Permisos de administrador", admin, admin ? "OK" : "Necesarios para ventiladores");

        var pawn = Components.PawnIO.IsInstalled();
        Row("Sensores y ventiladores", pawn, pawn ? "Módulo listo" : "Falta el módulo (solo GPU)");
        var rgb = Components.FindOpenRgb();
        Row("Iluminación RGB", rgb != null, rgb != null ? "Módulo listo" : "Falta el módulo");
        var eq = Components.EqualizerApo.IsInstalled();
        Row("Ecualizador", eq, eq ? "Módulo listo" : "Falta el módulo");


        var audio = Hub.Audio.DefaultOutput?.FriendlyName ?? "—";
        Row("Salida de audio", true, audio);

        var vendors = VendorApps.Running().Select(v => v.Name).ToList();
        Row("Apps del fabricante", vendors.Count == 0 ? true : false,
            vendors.Count == 0 ? "Ninguna en conflicto" : string.Join(", ", vendors));

        Status.Text = pawn && rgb != null && eq ? "Todo listo" : "Puedes iniciar igualmente; instala lo que falte para tener todas las funciones";
        StartBtn.IsEnabled = true;

        _release = await Updater.LatestAsync();
        if (_release != null && _release.Version > Updater.Current)
        {
            Row("Actualización", false, $"Disponible {_release.Tag}");
            UpdateBtn.Content = $"Actualizar a {_release.Tag} desde GitHub";
            UpdateBtn.Visibility = Visibility.Visible;
        }
        else Row("Versión", _release != null ? true : null, _release != null ? "Al día" : "Sin conexión con GitHub");
    }

    Updater.Release? _release;

    async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_release == null) return;
        UpdateBtn.IsEnabled = StartBtn.IsEnabled = false;
        try
        {
            await Updater.SelfUpdateAsync(_release, new Progress<double>(v => UpdateBtn.Content = $"Descargando… {v:P0}"));
        }
        catch (Exception ex)
        {
            Status.Text = "No se pudo actualizar: " + ex.Message;
            UpdateBtn.IsEnabled = StartBtn.IsEnabled = true;
        }
    }

    void Start_Click(object sender, RoutedEventArgs e)
    {
        Settings.Current.SkipLauncher = Skip.IsChecked == true;
        Settings.Current.Save();
        var main = new MainWindow();
        main.Show();
        Close();
    }

    void Components_Click(object sender, RoutedEventArgs e)
    {
        var w = new ComponentsWindow { Owner = this };
        w.ShowDialog();
        _ = CheckAsync();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
