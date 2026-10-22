using Microsoft.AspNetCore.Identity;

namespace NguyenDuongNhutKhang_app.Data;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Employee = "Employee";
}

public class AppUser : IdentityUser
{
    public string FullName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public int? CompanyId { get; set; }
    public string? GoogleSubject { get; set; }
    public string? Position { get; set; }   // chức vụ
    public string? Department { get; set; } // phòng ban
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Company
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Địa điểm làm việc: vị trí GPS, bán kính hợp lệ, giờ vào/ra chuẩn và khóa bí mật sinh mã QR xoay vòng.</summary>
public class Workplace
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Name { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMeters { get; set; } = 150;
    public int StartMinute { get; set; } = 8 * 60;   // phút kể từ 00:00 (giờ Việt Nam)
    public int EndMinute { get; set; } = 17 * 60;
    public int GraceMinutes { get; set; } = 10;      // trễ dưới ngưỡng này vẫn tính đúng giờ
    public string QrSecret { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Attendance
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string UserId { get; set; } = "";
    public int WorkplaceId { get; set; }
    public DateTime CheckIn { get; set; }
    public DateTime? CheckOut { get; set; }
    public double? InLat { get; set; }
    public double? InLng { get; set; }
    public double? OutLat { get; set; }
    public double? OutLng { get; set; }
    public string Status { get; set; } = "OnTime"; // OnTime | Late
    public int LateMinutes { get; set; }
    public int EarlyLeaveMinutes { get; set; }
    public string? Note { get; set; }
}

public class LocationPoint
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Accuracy { get; set; }
    public int? Battery { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ChatMessage
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string SenderId { get; set; } = "";
    public string Kind { get; set; } = "text"; // text | call
    public string Text { get; set; } = "";
    public string? Channel { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }
}

public class OtpCode
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Purpose { get; set; } = ""; // login | verify-email
    public string CodeHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public bool Used { get; set; }
}

/// <summary>Mã mời 6 số để nhân viên tự đăng ký vào đúng công ty. Dùng một lần, hết hạn sau 24 giờ.</summary>
public class CompanyInvite
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Code { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
