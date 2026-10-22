using System.Net.Http.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace NguyenDuongNhutKhang_app.Services;

public class EmailNotConfiguredException : Exception;

public class EmailSender(IOptions<SmtpOptions> options, IOptions<BrevoOptions> brevo)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly SmtpOptions _o = options.Value;
    private readonly BrevoOptions _b = brevo.Value;

    public async Task SendAsync(string to, string subject, string html, CancellationToken ct = default)
    {
        if (_b.Configured) { await SendBrevoAsync(to, subject, html, ct); return; }
        if (!_o.Configured) throw new EmailNotConfiguredException();
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_o.FromName, _o.User));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();
        using var client = new SmtpClient();
        await client.ConnectAsync(_o.Host, _o.Port, _o.Security switch { "Ssl" => SecureSocketOptions.SslOnConnect, "None" => SecureSocketOptions.None, _ => SecureSocketOptions.StartTls }, ct);
        await client.AuthenticateAsync(_o.User, _o.Password, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }

    private async Task SendBrevoAsync(string to, string subject, string html, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        req.Headers.Add("api-key", _b.ApiKey);
        req.Content = JsonContent.Create(new
        {
            sender = new { name = _b.SenderName, email = _b.SenderEmail },
            to = new[] { new { email = to } },
            subject,
            htmlContent = html
        });
        using var res = await Http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Brevo trả về {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync(ct)}");
    }

    public Task SendOtpAsync(string to, string code, string purpose) =>
        SendAsync(to, $"ChamCong - Mã xác thực {code}",
            "<div style=\"font-family:sans-serif\"><h2>ChamCong</h2>" +
            $"<p>Mã OTP {purpose} của bạn:</p>" +
            $"<p style=\"font-size:32px;letter-spacing:6px\"><b>{code}</b></p>" +
            "<p>Mã có hiệu lực 5 phút. Không chia sẻ mã này cho bất kỳ ai.</p></div>");
}
