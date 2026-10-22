using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace NguyenDuongNhutKhang_app.Services;

public class SmsNotConfiguredException : Exception;

public class SmsSender(IOptions<TwilioOptions> options, IOptions<SpeedSmsOptions> speed)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly TwilioOptions _o = options.Value;
    private readonly SpeedSmsOptions _s = speed.Value;

    /// <summary>Chuẩn hóa về E.164: 0912345678 → +84912345678, 84912345678 → +84912345678. Trả null nếu không hợp lệ.</summary>
    public static string? Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var p = new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (p.StartsWith('+')) { }
        else if (p.StartsWith("00")) p = "+" + p[2..];
        else if (p.StartsWith('0')) p = "+84" + p[1..];
        else if (p.StartsWith("84")) p = "+" + p;
        else return null;
        return p.Length is >= 10 and <= 16 && p[1..].All(char.IsDigit) ? p : null;
    }

    public static string Mask(string phone) => phone.Length <= 7 ? phone : phone[..4] + new string('*', phone.Length - 7) + phone[^3..];

    public async Task SendAsync(string to, string text, CancellationToken ct = default)
    {
        if (_s.Configured) { await SendSpeedSmsAsync(to, text, ct); return; }
        if (!_o.Configured) throw new SmsNotConfiguredException();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_o.BaseUrl.TrimEnd('/')}/2010-04-01/Accounts/{_o.AccountSid}/Messages.json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_o.AccountSid}:{_o.AuthToken}")));
        var form = new Dictionary<string, string> { ["To"] = to, ["Body"] = text };
        form[_o.From.StartsWith("MG") ? "MessagingServiceSid" : "From"] = _o.From;
        req.Content = new FormUrlEncodedContent(form);
        using var res = await Http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Twilio trả về {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync(ct)}");
    }

    private async Task SendSpeedSmsAsync(string to, string text, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_s.BaseUrl.TrimEnd('/')}/index.php/sms/send");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_s.AccessToken}:x")));
        var body = new Dictionary<string, object> { ["to"] = new[] { to.TrimStart('+') }, ["content"] = text, ["type"] = _s.Type };
        if (!string.IsNullOrWhiteSpace(_s.Sender)) body["sender"] = _s.Sender;
        req.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var res = await Http.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode || !json.Contains("\"success\""))
            throw new InvalidOperationException($"SpeedSMS trả về {(int)res.StatusCode}: {json}");
    }

    // Chỉ dùng ký tự ASCII để tin nhắn không bị chuyển sang mã hóa Unicode (giới hạn 70 ký tự/tin).
    public Task SendOtpAsync(string to, string code) =>
        SendAsync(to, $"ChamCong: ma OTP cua ban la {code}. Het han sau 5 phut, khong chia se cho ai.");
}
