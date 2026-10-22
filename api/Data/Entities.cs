using Microsoft.AspNetCore.Identity;

namespace NguyenDuongNhutKhang_app.Data;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Parent = "Parent";
    public const string Child = "Child";
}

public class AppUser : IdentityUser
{
    public string FullName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public int? FamilyId { get; set; }
    public string? GoogleSubject { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Family
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SafeZone
{
    public int Id { get; set; }
    public int FamilyId { get; set; }
    public string Name { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMeters { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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

/// <summary>Trạng thái trong/ngoài vùng an toàn gần nhất của từng trẻ, để phát hiện lúc vượt ranh giới.</summary>
public class ZoneState
{
    public string UserId { get; set; } = "";
    public int ZoneId { get; set; }
    public bool Inside { get; set; }
}

public class Alert
{
    public int Id { get; set; }
    public int FamilyId { get; set; }
    public string ChildId { get; set; } = "";
    public string Type { get; set; } = "SOS"; // SOS | ExitZone | EnterZone
    public string Message { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? Battery { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsResolved { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ChatMessage
{
    public int Id { get; set; }
    public int FamilyId { get; set; }
    public string SenderId { get; set; } = "";
    public string Kind { get; set; } = "text"; // text | call
    public string Text { get; set; } = "";
    public string? Channel { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }
}

public class TranslationRecord
{
    public int Id { get; set; }
    public int FamilyId { get; set; }
    public string ChildId { get; set; } = "";
    public string Kind { get; set; } = "text"; // text | photo | realtime
    public string SourceLang { get; set; } = "";
    public string TargetLang { get; set; } = "";
    public string SourceText { get; set; } = "";
    public string TranslatedText { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
