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

/// <summary>Gửi SMS OTP qua SpeedSMS (nhà cung cấp Việt Nam). Có AccessToken thì ưu tiên dùng thay Twilio.
/// Type: 2 = đầu số ngẫu nhiên, 3 = brandname, 4 = brandname mặc định, 5 = ứng dụng Android (dùng SIM điện thoại của bạn).</summary>
/// <summary>Chế độ demo: không gửi SMS/email thật, trả mã OTP trong header X-Demo-Otp để app hiển thị. Chỉ bật khi báo cáo.</summary>
public class DemoOptions
{
    public bool ShowOtp { get; set; }
}

public class SpeedSmsOptions
{
    public string AccessToken { get; set; } = "";
    public int Type { get; set; } = 2;
    public string Sender { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.speedsms.vn";
    public bool Configured => !string.IsNullOrWhiteSpace(AccessToken);
}

/// <summary>Gửi SMS OTP qua Twilio. BaseUrl chỉ đổi khi kiểm thử với máy chủ giả.</summary>
public class TwilioOptions
{
    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string From { get; set; } = ""; // số Twilio (+1...) hoặc Messaging Service SID (MG...)
    public string BaseUrl { get; set; } = "https://api.twilio.com";
    public bool Configured => !string.IsNullOrWhiteSpace(AccountSid) && !string.IsNullOrWhiteSpace(AuthToken) && !string.IsNullOrWhiteSpace(From);
}
