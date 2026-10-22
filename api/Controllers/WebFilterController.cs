using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;

namespace NguyenDuongNhutKhang_app.Controllers;

public record BlockDto([Required, StringLength(200, MinimumLength = 3)] string Domain, string? ChildId);

/// <summary>Danh sách tên miền bị chặn. Phụ huynh quản lý; trẻ đọc danh sách áp dụng cho mình.</summary>
[Route("api/webfilter")]
public class WebFilterController(AppDbContext db) : ApiBase
{
    [Authorize(Roles = Roles.Parent), HttpGet]
    public async Task<IActionResult> List([FromQuery] string? childId) =>
        Ok(await db.BlockedSites.Where(b => b.FamilyId == FamilyId && (childId == null || b.ChildId == null || b.ChildId == childId))
            .OrderBy(b => b.Domain).ToListAsync());

    [Authorize(Roles = Roles.Parent), HttpPost]
    public async Task<IActionResult> Add(BlockDto dto)
    {
        var domain = Normalize(dto.Domain);
        if (domain == null) return Fail(400, "Tên miền không hợp lệ.");
        var childId = string.IsNullOrWhiteSpace(dto.ChildId) ? null : dto.ChildId;
        if (childId != null && !await db.Users.AnyAsync(u => u.Id == childId && u.FamilyId == FamilyId)) return NotFound();
        if (await db.BlockedSites.AnyAsync(b => b.FamilyId == FamilyId && b.ChildId == childId && b.Domain == domain))
            return Fail(400, "Tên miền đã có trong danh sách.");
        var b = new BlockedSite { FamilyId = FamilyId, ChildId = childId, Domain = domain };
        db.BlockedSites.Add(b);
        await db.SaveChangesAsync();
        return Ok(b);
    }

    [Authorize(Roles = Roles.Parent), HttpDelete("{id:int}")]
    public async Task<IActionResult> Remove(int id)
    {
        var b = await db.BlockedSites.FirstOrDefaultAsync(x => x.Id == id && x.FamilyId == FamilyId);
        if (b == null) return NotFound();
        db.BlockedSites.Remove(b);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Trẻ lấy danh sách tên miền bị chặn (chung cả nhà + riêng mình).</summary>
    [Authorize(Roles = Roles.Child), HttpGet("mine")]
    public async Task<IActionResult> Mine() =>
        Ok(await db.BlockedSites.Where(b => b.FamilyId == FamilyId && (b.ChildId == null || b.ChildId == UserId))
            .Select(b => b.Domain).Distinct().ToListAsync());

    private static string? Normalize(string input)
    {
        var d = input.Trim().ToLowerInvariant();
        if (Uri.TryCreate(d.Contains("://") ? d : "http://" + d, UriKind.Absolute, out var u)) d = u.Host;
        if (d.StartsWith("www.")) d = d[4..];
        return d.Contains('.') && d.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') ? d : null;
    }
}
