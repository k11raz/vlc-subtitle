using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;

namespace SubLingo.Services;

public class TranslateService
{
    private readonly HttpClient _http = new();
    private string? _deepLKey;
    private string _libreTranslateUrl = "http://localhost:5000";

    public TranslateService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36");
    }

    public void SetDeepLKey(string key) => _deepLKey = key;
    
    public void SetLibreTranslateUrl(string url) => _libreTranslateUrl = url;

    public async Task<string> TranslateSentence(string text, string targetLang = "tr")
    {
        // 1. LibreTranslate (offline, local)
        var libreResult = await LibreTranslate(text, targetLang);
        if (libreResult != null) return libreResult;

        // 2. DeepL (online, key varsa)
        if (!string.IsNullOrEmpty(_deepLKey))
        {
            var deepl = await DeepLTranslate(text, targetLang);
            if (deepl != null) return deepl;
        }

        // 3. Google Translate (online, ücretsiz)
        var google = await GoogleTranslate(text, targetLang);
        if (google == null) return "⚠ Çeviri yapılamadı";
        if (google == text) return "⚠ Çeviri başarısız";
        return google;
    }

    public async Task<string> TranslateWord(string word, string targetLang = "tr")
    {
        // 1. LibreTranslate
        var libre = await LibreTranslate(word.ToLower().Trim(), targetLang);
        if (libre != null && libre != word) return libre;

        // 2. Google Translate
        var result = await GoogleTranslate(word.ToLower().Trim(), targetLang);
        if (result == null || result == word) return "?";
        return result;
    }
    
    private async Task<string?> LibreTranslate(string text, string targetLang)
    {
        try
        {
            var payload = new
            {
                q = text,
                source = "en",
                target = targetLang
            };

            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8,
                "application/json");

            var res = await _http.PostAsync($"{_libreTranslateUrl}/translate", content);
            if (!res.IsSuccessStatusCode) return null;

            var json = await res.Content.ReadFromJsonAsync<LibreTranslateResponse>();
            return json?.translatedText;
        }
        catch { return null; }
    }

    private record LibreTranslateResponse(string translatedText);

    private async Task<string?> GoogleTranslate(string text, string targetLang)
    {
        try
        {
            var url = $"https://translate.googleapis.com/translate_a/single" +
                      $"?client=gtx&sl=auto&tl={targetLang}&dt=t&q={HttpUtility.UrlEncode(text)}";

            var response = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var parts = new List<string>();

            foreach (var seg in doc.RootElement[0].EnumerateArray())
            {
                var t = seg[0].GetString();
                if (!string.IsNullOrWhiteSpace(t)) parts.Add(t);
            }

            return string.Join("", parts);
        }
        catch { return null; }
    }

    private async Task<string?> DeepLTranslate(string text, string targetLang)
    {
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["auth_key"] = _deepLKey!,
                ["text"] = text,
                ["target_lang"] = targetLang.ToUpper()
            });

            var res = await _http.PostAsync("https://api-free.deepl.com/v2/translate", content);
            var json = await res.Content.ReadFromJsonAsync<DeepLResponse>();
            return json?.translations?.FirstOrDefault()?.text;
        }
        catch { return null; }
    }

    private record DeepLResponse(List<DeepLTranslation>? translations);
    private record DeepLTranslation(string text);
    
    public async Task<bool> CheckLibreTranslate()
    {
        try
        {
            var res = await _http.GetAsync($"{_libreTranslateUrl}/languages");
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
