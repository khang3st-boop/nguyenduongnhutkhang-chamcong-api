using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;

namespace NguyenDuongNhutKhang_app.Controllers;

public static class Approval
{
    public const string Type = "approval";
    public const string Pending = "pending";
}

/// <summary>Quản lý đăng ký mới phải được admin hoặc một quản lý khác duyệt trước khi đăng nhập được.</summary>
[Authorize(Roles = Roles.Admin + "," + Roles.Manager), Route("api/[controller]")]
public class ApprovalsController(UserManager<AppUser> users, AppDbContext db) : ApiBase
{
    [HttpGet]
    public async Task<IActionResult> Pending()
    {
        var ids = await db.UserClaims.Where(c => c.ClaimType == Approval.Type && c.ClaimValue == Approval.Pending).Select(c => c.UserId).ToListAsync();
        var rows = await db.Users.Where(u => ids.Contains(u.Id)).OrderBy(u => u.CreatedAt)
            .Select(u => new { u.Id, userName = u.UserName, u.FullName, u.Email, u.EmailConfirmed, u.CreatedAt }).ToListAsync();
        return Ok(rows);
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(string id)
    {
        var u = await users.FindByIdAsync(id);
        if (u == null || id == UserId) return NotFound();
        await users.RemoveClaimAsync(u, new Claim(Approval.Type, Approval.Pending));
        return NoContent();
    }

    [HttpPost("{id}/reject")]
    public async Task<IActionResult> Reject(string id)
    {
        var u = await users.FindByIdAsync(id);
        if (u == null || id == UserId) return NotFound();
        if (!(await users.GetClaimsAsync(u)).Any(c => c.Type == Approval.Type && c.Value == Approval.Pending))
            return Fail(400, "Tài khoản này đã được duyệt.");
        db.OtpCodes.RemoveRange(db.OtpCodes.Where(o => o.UserId == id));
        db.Companies.RemoveRange(db.Companies.Where(c => c.OwnerId == id));
        await db.SaveChangesAsync();
        await users.DeleteAsync(u);
        return NoContent();
    }
}
