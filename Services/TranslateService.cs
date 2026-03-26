using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;

namespace SubLingo.Services;

public class TranslateService
{
    private readonly HttpClient _http = new();
    private string? _deepLKey;

    public TranslateService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36");
    }

    public void SetDeepLKey(string key) => _deepLKey = key;

    public async Task<string> TranslateSentence(string text, string targetLang = "tr")
    {
        if (!string.IsNullOrEmpty(_deepLKey))
        {
            var result = await DeepLTranslate(text, targetLang);
            if (result != null) return result;
        }

        return await GoogleTranslate(text, targetLang) ?? text;
    }

    public async Task<string> TranslateWord(string word, string targetLang = "tr")
        => await GoogleTranslate(word.ToLower().Trim(), targetLang) ?? word;

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
}
