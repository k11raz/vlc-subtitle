using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SubLingo.Services;

public class SubtitleEntry
{
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public string Text { get; set; } = "";
}

public class SubtitleService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private List<SubtitleEntry> _entries = [];
    private string _loadedPath = "";

    public void LoadSrt(string path)
    {
        if (_loadedPath == path && _entries.Count > 0) return;
        var content = File.ReadAllText(path);
        _entries = path.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase)
            ? ParseVtt(content)
            : ParseSrt(content);
        _loadedPath = path;
    }

    public async Task<double?> GetVlcTime(int port, string password = "")
{
    // Önce DBus dene (çok daha hassas)
    var dbusTime = await GetVlcTimeDBus();
    if (dbusTime != null) return dbusTime;

    // Fallback: HTTP API
    try
    {
        var req = new HttpRequestMessage(HttpMethod.Get,
            $"http://localhost:{port}/requests/status.json");

        if (!string.IsNullOrEmpty(password))
        {
            var creds = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes($":{password}"));
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", creds);
        }

        var res = await _http.SendAsync(req);
        var json = await res.Content.ReadFromJsonAsync<VlcStatus>();
        if (json == null) return null;
        if (json.length > 0 && json.position > 0)
            return json.position * json.length;
        return json.time;
    }
    catch { return null; }
}

private async Task<double?> GetVlcTimeDBus()
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dbus-send",
            Arguments = "--print-reply --dest=org.mpris.MediaPlayer2.vlc " +
                        "/org/mpris/MediaPlayer2 " +
                        "org.freedesktop.DBus.Properties.Get " +
                        "string:\"org.mpris.MediaPlayer2.Player\" " +
                        "string:\"Position\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = System.Diagnostics.Process.Start(psi);
        if (process == null) return null;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        // "variant       int64 2400371" satırını parse et
        var m = System.Text.RegularExpressions.Regex.Match(output, @"int64\s+(\d+)");
        if (!m.Success) return null;

        var microseconds = long.Parse(m.Groups[1].Value);
        return microseconds / 1_000_000.0; // saniyeye çevir
    }
    catch { return null; }
}

    public SubtitleEntry? GetSubtitleAt(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return _entries.FirstOrDefault(e => ts >= e.Start && ts <= e.End);
    }

    // ─── SRT Parser ─────────────────────────────────────────────────────────

    private static List<SubtitleEntry> ParseSrt(string content)
    {
        var entries = new List<SubtitleEntry>();
        content = content.Replace("\r\n", "\n").Replace("\r", "\n");

        foreach (var block in content.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Trim().Split('\n');
            if (lines.Length < 3) continue;

            var m = Regex.Match(lines[1],
                @"(\d{2}):(\d{2}):(\d{2}),(\d{3})\s*-->\s*(\d{2}):(\d{2}):(\d{2}),(\d{3})");
            if (!m.Success) continue;

            var start = new TimeSpan(0,
                int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value));
            var end = new TimeSpan(0,
                int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value),
                int.Parse(m.Groups[7].Value), int.Parse(m.Groups[8].Value));

            var text = Regex.Replace(string.Join(" ", lines.Skip(2)), "<[^>]+>", "").Trim();
            if (!string.IsNullOrWhiteSpace(text))
                entries.Add(new SubtitleEntry { Start = start, End = end, Text = text });
        }

        return entries;
    }

    // ─── VTT Parser ─────────────────────────────────────────────────────────

    private static List<SubtitleEntry> ParseVtt(string content)
    {
        var entries = new List<SubtitleEntry>();
        content = content.Replace("\r\n", "\n").Replace("\r", "\n");

        foreach (var block in content.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Trim().Split('\n');
            if (lines.Length < 2) continue;

            // WEBVTT header satırını atla
            var timeLine = lines.FirstOrDefault(l => l.Contains("-->"));
            if (timeLine == null) continue;

            // Format: 00:00:00.000 --> 00:00:00.000
            var m = Regex.Match(timeLine,
                @"(\d{1,2}):(\d{2}):(\d{2})\.(\d{3})\s*-->\s*(\d{1,2}):(\d{2}):(\d{2})\.(\d{3})");

            // Format: 00:00.000 --> 00:00.000 (saatsiz)
            if (!m.Success)
                m = Regex.Match(timeLine,
                    @"(\d{2}):(\d{2})\.(\d{3})\s*-->\s*(\d{2}):(\d{2})\.(\d{3})");

            if (!m.Success) continue;

            TimeSpan start, end;
            if (m.Groups.Count == 9) // saat:dakika:saniye.ms
            {
                start = new TimeSpan(0,
                    int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                    int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value));
                end = new TimeSpan(0,
                    int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value),
                    int.Parse(m.Groups[7].Value), int.Parse(m.Groups[8].Value));
            }
            else // dakika:saniye.ms
            {
                start = new TimeSpan(0, 0,
                    int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                    int.Parse(m.Groups[3].Value));
                end = new TimeSpan(0, 0,
                    int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value),
                    int.Parse(m.Groups[6].Value));
            }

            var timeLineIndex = Array.IndexOf(lines, timeLine);
            var textLines = lines.Skip(timeLineIndex + 1).ToArray();

            // VTT tag'lerini temizle: <i>, <b>, <c.color>, timestamp tag'leri vs
            var text = Regex.Replace(string.Join(" ", textLines), @"<[^>]+>", "").Trim();
            // NOTE, STYLE, REGION bloklarını atla
            if (string.IsNullOrWhiteSpace(text) ||
                text.StartsWith("NOTE") ||
                text.StartsWith("STYLE") ||
                text.StartsWith("REGION")) continue;

            entries.Add(new SubtitleEntry { Start = start, End = end, Text = text });
        }

        return entries;
    }
    
    public SubtitleEntry? GetNextSubtitle(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return _entries
            .Where(e => e.Start > ts)
            .OrderBy(e => e.Start)
            .FirstOrDefault();
    }
    private record VlcStatus(double time, double position, double length, string state);
}