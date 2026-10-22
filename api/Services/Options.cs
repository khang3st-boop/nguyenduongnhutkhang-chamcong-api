namespace NguyenDuongNhutKhang_app.Services;

public class SmtpOptions
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string Security { get; set; } = "StartTls"; // StartTls | Ssl | None (None chỉ dùng cho SMTP nội bộ khi kiểm thử)
    public string FromName { get; set; } = "ChamCong";
    public bool Configured => !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(Password);
}

/// <summary>Gửi mail qua API HTTPS của Brevo, dùng khi host chặn cổng SMTP (vd. Render gói miễn phí).</summary>
public class BrevoOptions
{
    public string ApiKey { get; set; } = "";
    public string SenderEmail { get; set; } = "";
    public string SenderName { get; set; } = "ChamCong";
    public bool Configured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(SenderEmail);
}

public class GoogleOptions
{
    /// <summary>Danh sách Web/Android/iOS Client ID hợp lệ làm audience của idToken.</summary>
    public string[] ClientIds { get; set; } = [];
}

public class AgoraOptions
{
    public string AppId { get; set; } = "";
    public string AppCertificate { get; set; } = "";
}

public class AdminSeedOptions
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string UserName { get; set; } = "admin";
}
