using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NguyenDuongNhutKhang_app.Data;

namespace NguyenDuongNhutKhang_app.Controllers;

public record PinDto([Required, RegularExpression(@"^\d{4}$")] string Pin, [RegularExpression(@"^\d{4}$")] string? CurrentPin = null);

/// <summary>Mã PIN 4 số của phụ huynh để bảo vệ màn hình cài đặt. Lưu dạng băm trên máy chủ.</summary>
[Authorize(Roles = Roles.Parent), Route("api/[controller]")]
public class FamilyController(AppDbContext db) : ApiBase
{
    private static readonly PasswordHasher<Family> Hasher = new();

    [HttpGet("pin")]
    public async Task<IActionResult> HasPin()
    {
        var f = await db.Families.FindAsync(FamilyId);
        return Ok(new { hasPin = !string.IsNullOrEmpty(f?.PinHash) });
    }

    /// <summary>Đặt PIN lần đầu, hoặc đổi PIN (phải gửi CurrentPin đúng).</summary>
    [HttpPut("pin")]
    public async Task<IActionResult> SetPin(PinDto dto)
    {
        var f = await db.Families.FindAsync(FamilyId);
        if (f == null) return NotFound();
        if (!string.IsNullOrEmpty(f.PinHash) &&
            (dto.CurrentPin == null || Hasher.VerifyHashedPassword(f, f.PinHash, dto.CurrentPin) == PasswordVerificationResult.Failed))
            return Fail(400, "PIN hiện tại không đúng.");
        f.PinHash = Hasher.HashPassword(f, dto.Pin);
        await db.SaveChangesAsync();
        return Ok(new { status = true });
    }

    [HttpPost("pin/verify")]
    public async Task<IActionResult> Verify(PinDto dto)
    {
        var f = await db.Families.FindAsync(FamilyId);
        if (f == null || string.IsNullOrEmpty(f.PinHash)) return Fail(400, "Chưa đặt PIN.");
        var ok = Hasher.VerifyHashedPassword(f, f.PinHash, dto.Pin) != PasswordVerificationResult.Failed;
        return ok ? Ok(new { status = true }) : Fail(400, "PIN không đúng.");
    }
}
