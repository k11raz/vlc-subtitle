using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace SubLingo.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(AppConfig config)
    {
        InitializeComponent();

        TbSrtPath.Text = config.SrtPath;
        TbVlcPort.Text = config.VlcPort.ToString();
        TbVlcPassword.Text = config.VlcPassword;
        TbDeepL.Text = config.DeepLKey;

        // Dil seçimi
        foreach (var obj in CbLang.Items)
        {
            if (obj is ComboBoxItem item && item.Tag?.ToString() == config.TargetLang)
            {
                CbLang.SelectedItem = item;
                break;
            }
        }

        if (CbLang.SelectedIndex < 0)
            CbLang.SelectedIndex = 0;
    }

    private async void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "SRT Dosyasını Seç",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Altyazı") { Patterns = new[] { "*.srt", "*.vtt" } },
                new FilePickerFileType("Tüm Dosyalar") { Patterns = new[] { "*" } }
            }
        });

        if (files.Count > 0)
            TbSrtPath.Text = files[0].Path.LocalPath;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TbSrtPath.Text))
        {
            // Basit uyarı — ileride daha şık yapılabilir
            TbSrtPath.BorderBrush = Avalonia.Media.Brushes.Red;
            return;
        }

        var lang = (CbLang.SelectedItem as ComboBoxItem)?.Tag as string ?? "tr";

        var config = new AppConfig
        {
            SrtPath = TbSrtPath.Text.Trim(),
            VlcPort = int.TryParse(TbVlcPort.Text, out var port) ? port : 8080,
            VlcPassword = TbVlcPassword.Text?.Trim() ?? "",
            TargetLang = lang,
            DeepLKey = TbDeepL.Text?.Trim() ?? ""
        };

        Close(config);
    }
}
