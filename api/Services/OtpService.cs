using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;

namespace NguyenDuongNhutKhang_app.Services;

public class OtpService(AppDbContext db, IConfiguration config)
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResendGap = TimeSpan.FromSeconds(30);

    private string Hash(string userId, string purpose, string code)
    {
        var key = Encoding.UTF8.GetBytes(config["JWTKey:Secret"]!);
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{userId}|{purpose}|{code}")));
    }

    /// <summary>Tạo mã mới (vô hiệu mã cũ). Trả về null nếu gửi lại quá nhanh.</summary>
    public async Task<string?> IssueAsync(string userId, string purpose)
    {
        var last = await db.OtpCodes.Where(o => o.UserId == userId && o.Purpose == purpose)
            .OrderByDescending(o => o.Id).FirstOrDefaultAsync();
        if (last != null && !last.Used && last.ExpiresAt - Lifetime + ResendGap > DateTime.UtcNow) return null;
        db.OtpCodes.RemoveRange(db.OtpCodes.Where(o => o.UserId == userId && o.Purpose == purpose));
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.OtpCodes.Add(new OtpCode
        {
            UserId = userId, Purpose = purpose, CodeHash = Hash(userId, purpose, code),
            ExpiresAt = DateTime.UtcNow.Add(Lifetime)
        });
        await db.SaveChangesAsync();
        return code;
    }

    public async Task<bool> VerifyAsync(string userId, string purpose, string code)
    {
        var otp = await db.OtpCodes.Where(o => o.UserId == userId && o.Purpose == purpose && !o.Used)
            .OrderByDescending(o => o.Id).FirstOrDefaultAsync();
        if (otp == null || otp.ExpiresAt < DateTime.UtcNow || otp.Attempts >= MaxAttempts) return false;
        otp.Attempts++;
        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(otp.CodeHash), Encoding.UTF8.GetBytes(Hash(userId, purpose, code.Trim())));
        if (ok) otp.Used = true;
        await db.SaveChangesAsync();
        return ok;
    }
}
