using System.Security.Cryptography;
using System.Text;
using NguyenDuongNhutKhang_app.Data;

namespace NguyenDuongNhutKhang_app.Services;

/// <summary>Mã QR chấm công xoay vòng: CC.{workplaceId}.{cửa sổ 30s}.{chữ ký HMAC}. Mã cũ quá 1 cửa sổ bị từ chối.</summary>
public class QrService
{
    public const int WindowSeconds = 30;

    private static long Unix(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();

    private static string Sign(Workplace z, long window) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(z.QrSecret), Encoding.UTF8.GetBytes($"{z.Id}|{window}")))[..16];

    public (string payload, int secondsLeft) Create(Workplace z)
    {
        var s = Unix(DateTime.UtcNow);
        var w = s / WindowSeconds;
        return ($"CC.{z.Id}.{w}.{Sign(z, w)}", WindowSeconds - (int)(s % WindowSeconds));
    }

    /// <summary>Đọc workplaceId từ payload; trả null nếu sai định dạng.</summary>
    public static int? ParseWorkplaceId(string payload)
    {
        var p = payload.Trim().Split('.');
        return p.Length == 4 && p[0] == "CC" && int.TryParse(p[1], out var id) ? id : null;
    }

    public bool Verify(Workplace z, string payload)
    {
        var p = payload.Trim().Split('.');
        if (p.Length != 4 || p[0] != "CC" || !long.TryParse(p[2], out var w)) return false;
        var cur = Unix(DateTime.UtcNow) / WindowSeconds;
        if (w > cur || cur - w > 1) return false; // cho phép trễ tối đa 1 cửa sổ (độ trễ mạng)
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(p[3]), Encoding.UTF8.GetBytes(Sign(z, w)));
    }
}
