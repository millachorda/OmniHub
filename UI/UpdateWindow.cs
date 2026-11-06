using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OmniHub.Core;

namespace OmniHub.UI;

/// <summary>
/// Al abrir OmniHub solo se mira si hay una versión nueva en GitHub.
/// Si la hay, se descarga, se instala y se vuelve a abrir sola; si no, se abre la app.
/// </summary>
public class UpdateWindow : Window
{
    readonly TextBlock _text = new() { FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 10) };
    readonly ProgressBar _bar = new() { Height = 4, Width = 260, Maximum = 1, IsIndeterminate = true, BorderThickness = new Thickness(0) };

    public UpdateWindow()
    {
        Title = "OmniHub";
        Width = 380; Height = 230;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        ShowInTaskbar = true; Topmost = false;
        var app = Application.Current;
        _text.Foreground = (Brush)app.FindResource("Muted");
        _bar.Foreground = (Brush)app.FindResource("Accent");
        _bar.Background = (Brush)app.FindResource("Line");

        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")), Width = 64, Height = 64 });
        sp.Children.Add(new TextBlock { Text = "OmniHub", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = (Brush)app.FindResource("Text"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
        sp.Children.Add(_text);
        sp.Children.Add(_bar);
        Content = new Border
        {
            CornerRadius = new CornerRadius(16), Background = (Brush)app.FindResource("Bg"),
            BorderBrush = (Brush)app.FindResource("Line"), BorderThickness = new Thickness(1), Child = sp
        };
        FontFamily = (FontFamily)app.FindResource("UiFont");
        _text.Text = "Buscando actualizaciones…";
        // Abrir minimizado: la comprobación se hace igual pero sin enseñar nada (salvo si hay que actualizar)
        if (Settings.Current.StartMinimized) { Opacity = 0; ShowInTaskbar = false; }
        Loaded += async (_, _) => await RunAsync();
    }

    async Task RunAsync()
    {
        Updater.Release? rel = null;
        try
        {
            var check = Updater.LatestAsync();
            if (await Task.WhenAny(check, Task.Delay(5000)) == check) rel = check.Result;
        }
        catch { }

        if (rel != null && rel.Version > Updater.Current)
        {
            Opacity = 1; ShowInTaskbar = true;
            _text.Text = $"Actualizando a {rel.Tag}…";
            _bar.IsIndeterminate = false;
            try
            {
                await Updater.SelfUpdateAsync(rel, new Progress<double>(v => { _bar.Value = v; _text.Text = $"Actualizando a {rel.Tag}… {v:P0}"; }));
                return; // la app se cierra y se vuelve a abrir ya actualizada
            }
            catch (Exception e)
            {
                _text.Text = "No se pudo actualizar: " + e.Message;
                await Task.Delay(2500);
            }
        }

        var main = new MainWindow();
        if (Settings.Current.StartMinimized) main.StartHidden(); else main.Show();
        Close();
    }
}
