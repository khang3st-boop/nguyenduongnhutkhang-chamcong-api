using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NguyenDuongNhutKhang_app.Controllers;

public record TranslateDto([Required, MinLength(1)] string[] Texts, string? Target = "vi", string? Source = "auto");

/// <summary>Dịch văn bản (tin nhắn chat, chữ nhận từ camera). Dùng dịch vụ dịch công khai của Google, không cần khóa API.</summary>
[Authorize, Route("api/[controller]")]
public class TranslateController(ILogger<TranslateController> log) : ApiBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly HashSet<string> Langs = ["vi", "en", "ja", "ko", "zh-CN", "zh-TW", "fr", "de", "es", "th", "ru", "it", "pt", "id"];

    // POST api/Translate : { texts: ["Hello"], target: "vi", source: "auto" } -> { items: [{ text, translated, detected }] }
    [HttpPost]
    public async Task<IActionResult> Translate(TranslateDto dto, CancellationToken ct)
    {
        var target = dto.Target ?? "vi";
        var source = string.IsNullOrWhiteSpace(dto.Source) ? "auto" : dto.Source;
        if (!Langs.Contains(target)) return Fail(400, "Ngôn ngữ đích không được hỗ trợ.");
        if (source != "auto" && !Langs.Contains(source)) return Fail(400, "Ngôn ngữ nguồn không được hỗ trợ.");
        var texts = dto.Texts.Select(t => (t ?? "").Trim()).ToArray();
        if (texts.Length > 30 || texts.Any(t => t.Length > 2000)) return Fail(400, "Tối đa 30 đoạn, mỗi đoạn 2000 ký tự.");

        try
        {
            var items = await Task.WhenAll(texts.Select(async t =>
            {
                if (t.Length == 0) return new { text = t, translated = t, detected = "" };
                var (translated, detected) = await One(t, source, target, ct);
                return new { text = t, translated, detected };
            }));
            return Ok(new { status = true, target, items });
        }
        catch (Exception ex)
        {
            log.LogWarning("Dịch thất bại: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return Fail(502, "Không dịch được lúc này, vui lòng thử lại.");
        }
    }

    // Thử Google trước (dịch tốt, tự nhận ngôn ngữ); bị chặn/giới hạn thì chuyển sang MyMemory.
    private static async Task<(string translated, string detected)> One(string text, string sl, string tl, CancellationToken ct)
    {
        try { return await Google(text, sl, tl, ct); }
        catch { return await MyMemory(text, sl, tl, ct); }
    }

    private static async Task<(string translated, string detected)> Google(string text, string sl, string tl, CancellationToken ct)
    {
        var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sl}&tl={tl}&dt=t&q={Uri.EscapeDataString(text)}";
        using var res = await Http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var sb = new StringBuilder();
        foreach (var seg in doc.RootElement[0].EnumerateArray())
            if (seg.ValueKind == JsonValueKind.Array && seg[0].ValueKind == JsonValueKind.String) sb.Append(seg[0].GetString());
        var detected = doc.RootElement.GetArrayLength() > 2 && doc.RootElement[2].ValueKind == JsonValueKind.String ? doc.RootElement[2].GetString()! : sl;
        return (sb.ToString(), detected);
    }

    private static async Task<(string translated, string detected)> MyMemory(string text, string sl, string tl, CancellationToken ct)
    {
        if (text.Length > 480) text = text[..480];
        var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair={(sl == "auto" ? "Autodetect" : sl)}|{tl}";
        using var res = await Http.GetAsync(url, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var translated = root.GetProperty("responseData").GetProperty("translatedText").GetString() ?? "";
        if (root.TryGetProperty("quotaFinished", out var q) && q.ValueKind == JsonValueKind.True) throw new InvalidOperationException("MyMemory hết hạn mức.");
        var detected = root.GetProperty("responseData").TryGetProperty("detectedLanguage", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : sl;
        return (System.Net.WebUtility.HtmlDecode(translated), detected);
    }
}
