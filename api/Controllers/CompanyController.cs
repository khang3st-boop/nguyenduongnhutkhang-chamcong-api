using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;

namespace NguyenDuongNhutKhang_app.Controllers;

public record NameDto([Required, StringLength(120)] string Name);

/// <summary>Công ty của quản lý: đổi tên và tạo mã mời 6 số cho nhân viên tự đăng ký.</summary>
[Authorize(Roles = Roles.Manager), Route("api/[controller]")]
public class CompanyController(AppDbContext db) : ApiBase
{
    [HttpGet]
    public async Task<IActionResult> Get() =>
        Ok(await db.Companies.Where(c => c.Id == CompanyId).Select(c => new { c.Id, c.Name }).FirstOrDefaultAsync());

    [HttpPut]
    public async Task<IActionResult> Rename(NameDto dto)
    {
        var c = await db.Companies.FindAsync(CompanyId);
        if (c == null) return NotFound();
        c.Name = dto.Name.Trim();
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Mã cũ chưa dùng của công ty bị thu hồi.</summary>
    [HttpPost("invite")]
    public async Task<IActionResult> CreateInvite()
    {
        db.CompanyInvites.RemoveRange(db.CompanyInvites.Where(i => i.CompanyId == CompanyId && (i.UsedAt != null || i.ExpiresAt < DateTime.UtcNow)));
        foreach (var old in db.CompanyInvites.Where(i => i.CompanyId == CompanyId && i.UsedAt == null)) old.UsedAt = DateTime.UtcNow;
        string code;
        do code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        while (await db.CompanyInvites.AnyAsync(i => i.Code == code && i.UsedAt == null && i.ExpiresAt > DateTime.UtcNow));
        var invite = new CompanyInvite { CompanyId = CompanyId, Code = code, ExpiresAt = DateTime.UtcNow.AddHours(24) };
        db.CompanyInvites.Add(invite);
        await db.SaveChangesAsync();
        return Ok(new { code, expiresAt = invite.ExpiresAt });
    }
}
