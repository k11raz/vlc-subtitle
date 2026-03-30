using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using SubLingo.Services;

namespace SubLingo.Views;

public partial class OverlayWindow : Window
{
    private System.Threading.Timer? _syncTimer;
    private readonly SubtitleService _subtitleSvc = new();
    private readonly TranslateService _translateSvc = new();
    private AppConfig _config;

    private CancellationTokenSource? _wordCts;
    private string _lastShownText = "";

    public OverlayWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;

        var screen = Screens.Primary;
        if (screen != null)
        {
            var w = screen.WorkingArea;
            Width = w.Width / 2;
            Height = 200;
            Position = new PixelPoint(w.X + w.Width / 4, w.Y + w.Height - 200);
        }

        this.Opened += async (s, e) =>
        {
            await StartLibreTranslate(); // önce LibreTranslate'i başlat
            if (_config.IsReady)
                await StartSync();
            else
                OpenSettings();
        };
    }
    
    private async Task StartLibreTranslate()
    {
        try
        {
            // Zaten çalışıyor mu?
            var res = await _translateSvc.CheckLibreTranslate();
            if (res) return;

            // Çalışmıyorsa podman ile başlat
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "podman",
                Arguments = "start libretranslate",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = System.Diagnostics.Process.Start(psi);
            if (process != null) await process.WaitForExitAsync();

            // Container yoksa oluştur
            if (process?.ExitCode != 0)
            {
                psi.Arguments = "run -d --name libretranslate -p 5000:5000 " +
                                "libretranslate/libretranslate --load-only en,tr";
                process = System.Diagnostics.Process.Start(psi);
                if (process != null) await process.WaitForExitAsync();
            }

            // Hazır olana kadar bekle (max 30 sn)
            SetStatus("LibreTranslate başlatılıyor...");
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(1000);
                if (await _translateSvc.CheckLibreTranslate())
                {
                    SetStatus("");
                    return;
                }
            }

            SetStatus("LibreTranslate başlatılamadı, Google Translate kullanılıyor");
        }
        catch { }
    }

    // ─── Sync ───────────────────────────────────────────────────────────────

    private async Task StartSync()
    {
        _syncTimer?.Dispose();
        await ScheduleNext();
    }

    private async Task ScheduleNext()
    {
        try
        {
            // YENİ
            var requestStart = DateTime.UtcNow;
            var time = await _subtitleSvc.GetVlcTime(_config.VlcPort, _config.VlcPassword);
            var requestMs = (DateTime.UtcNow - requestStart).TotalMilliseconds;
            if (time != null) time = time.Value + (requestMs / 1000.0); // gecikmeyi ekle
            if (time == null)
            {
                SetStatus("VLC'ye bağlanılamıyor...");
                _syncTimer = new System.Threading.Timer(
                    _ => _ = ScheduleNext(), null, 2000, Timeout.Infinite);
                return;
            }

            if (!File.Exists(_config.SrtPath))
            {
                SetStatus("SRT bulunamadı");
                return;
            }

            _subtitleSvc.LoadSrt(_config.SrtPath);
            SetStatus("");

            var current = _subtitleSvc.GetSubtitleAt(time.Value);
            var next = _subtitleSvc.GetNextSubtitle(time.Value);

            if (current != null && current.Text != _lastShownText)
            {
                _lastShownText = current.Text;
                var text = current.Text;
                Dispatcher.UIThread.Post(() => RenderSubtitle(text, "..."));
                _ = Task.Run(async () =>
                {
                    var tr = await _translateSvc.TranslateSentence(text, _config.TargetLang);
                    Dispatcher.UIThread.Post(() => TbTranslation.Text = tr);
                });
            }
            else if (current == null)
            {
                ClearSubtitle();
            }

            double delayMs;
            if (current != null)
                delayMs = (current.End.TotalSeconds - time.Value) * 1000;
            else if (next != null)
                delayMs = (next.Start.TotalSeconds - time.Value) * 1000;
            else
                delayMs = 1000;

            delayMs = Math.Max(50, delayMs - 50);

            _syncTimer = new System.Threading.Timer(
                _ => _ = ScheduleNext(), null, (int)delayMs, Timeout.Infinite);
        }
        catch (Exception ex)
        {
            SetStatus($"Hata: {ex.Message}");
            _syncTimer = new System.Threading.Timer(
                _ => _ = ScheduleNext(), null, 1000, Timeout.Infinite);
        }
    }

    // ─── Render ─────────────────────────────────────────────────────────────

    private void RenderSubtitle(string text, string translation)
    {
        WordsPanel.Children.Clear();
        TbTranslation.Text = translation;
        HidePopup();

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            var cleanWord = System.Text.RegularExpressions.Regex
                .Replace(token, @"[^a-zA-Z'-]", "").ToLower();

            var tb = new TextBlock
            {
                Text = token + " ",
                FontSize = 24,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                Cursor = new Cursor(StandardCursorType.Hand),
                Margin = new Thickness(2, 0)
            };

            if (!string.IsNullOrEmpty(cleanWord))
            {
                var word = cleanWord;
                tb.PointerEntered += async (s, e) =>
                {
                    tb.Foreground = new SolidColorBrush(Color.Parse("#A5B4FC"));
                    await ShowWordPopup(word, tb);
                };
                tb.PointerExited += (s, e) =>
                {
                    tb.Foreground = Brushes.White;
                    HidePopup();
                };
            }

            WordsPanel.Children.Add(tb);
        }
    }

    // ─── Word Popup ─────────────────────────────────────────────────────────

    private async Task ShowWordPopup(string word, TextBlock source)
    {
        _wordCts?.Cancel();
        _wordCts = new CancellationTokenSource();
        var token = _wordCts.Token;

        try
        {
            var translation = await _translateSvc.TranslateWord(word, _config.TargetLang);
            if (token.IsCancellationRequested) return;

            Dispatcher.UIThread.Post(() =>
            {
                PopupWord.Text = word;
                PopupTranslation.Text = translation;
                WordPopup.IsVisible = true;

                var pos = source.TranslatePoint(new Point(0, 0), this);
                if (pos.HasValue)
                {
                    WordPopup.Margin = new Thickness(
                        Math.Max(0, pos.Value.X - 10),
                        Math.Max(0, pos.Value.Y - 55),
                        0, 0);
                }
            });
        }
        catch { }
    }

    private void HidePopup()
    {
        _wordCts?.Cancel();
        Dispatcher.UIThread.Post(() => WordPopup.IsVisible = false);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private void ClearSubtitle()
    {
        Dispatcher.UIThread.Post(() =>
        {
            WordsPanel.Children.Clear();
            TbTranslation.Text = "";
            _lastShownText = "";
        });
    }

    private void SetStatus(string msg)
    {
        Dispatcher.UIThread.Post(() => TbStatus.Text = msg);
    }

    // ─── Ayarlar ────────────────────────────────────────────────────────────

    private void BtnSettings_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpenSettings();
    }

    private async void OpenSettings()
    {
        _syncTimer?.Dispose();
        var dlg = new SettingsWindow(_config);
        var result = await dlg.ShowDialog<AppConfig?>(this);

        if (result != null)
        {
            _config = result;
            _config.Save();
            if (!string.IsNullOrEmpty(_config.DeepLKey))
                _translateSvc.SetDeepLKey(_config.DeepLKey);
        }

        if (_config.IsReady)
            await StartSync();
    }
}