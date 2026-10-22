using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;

namespace NguyenDuongNhutKhang_app.Controllers;

/// <summary>Trang quản trị: thống kê hệ thống, quản lý/khóa/xóa tài khoản.</summary>
[Authorize(Roles = Roles.Admin), Route("api/[controller]")]
public class AdminController(UserManager<AppUser> users, AppDbContext db) : ApiBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var since = DateTime.UtcNow.AddDays(-1);
        var roleCounts = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                                group r by r.Name into g select new { role = g.Key, count = g.Count() }).ToListAsync();
        return Ok(new
        {
            roles = roleCounts,
            families = await db.Families.CountAsync(),
            zones = await db.SafeZones.CountAsync(),
            alerts24h = await db.Alerts.CountAsync(a => a.CreatedAt >= since),
            sosOpen = await db.Alerts.CountAsync(a => a.Type == "SOS" && !a.IsResolved),
            locations24h = await db.Locations.CountAsync(l => l.CreatedAt >= since),
            messages24h = await db.Messages.CountAsync(m => m.CreatedAt >= since)
        });
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery] string? q)
    {
        var query = from u in db.Users
                    join ur in db.UserRoles on u.Id equals ur.UserId
                    join r in db.Roles on ur.RoleId equals r.Id
                    select new { u, role = r.Name };
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.u.UserName!.Contains(q) || x.u.FullName.Contains(q) || (x.u.Email ?? "").Contains(q));
        var rows = await query.OrderByDescending(x => x.u.CreatedAt).Take(300).ToListAsync();
        return Ok(rows.Select(x => new
        {
            x.u.Id, userName = x.u.UserName, x.u.FullName, x.u.Email, x.role, x.u.FamilyId, x.u.CreatedAt,
            locked = x.u.LockoutEnd != null && x.u.LockoutEnd > DateTimeOffset.UtcNow
        }));
    }

    [HttpPut("users/{id}/lock")]
    public async Task<IActionResult> Lock(string id, [FromQuery] bool locked = true)
    {
        var u = await users.FindByIdAsync(id);
        if (u == null) return NotFound();
        if (id == UserId) return Fail(400, "Không thể khóa chính mình.");
        await users.SetLockoutEnabledAsync(u, true);
        await users.SetLockoutEndDateAsync(u, locked ? DateTimeOffset.MaxValue : null);
        return NoContent();
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var u = await users.FindByIdAsync(id);
        if (u == null) return NotFound();
        if (id == UserId) return Fail(400, "Không thể xóa chính mình.");
        if ((await users.GetRolesAsync(u)).Contains(Roles.Admin)) return Fail(400, "Không xóa tài khoản quản trị.");
        db.Locations.RemoveRange(db.Locations.Where(l => l.UserId == id));
        db.ZoneStates.RemoveRange(db.ZoneStates.Where(s => s.UserId == id));
        db.Alerts.RemoveRange(db.Alerts.Where(a => a.ChildId == id));
        db.Translations.RemoveRange(db.Translations.Where(t => t.ChildId == id));
        db.Messages.RemoveRange(db.Messages.Where(m => m.SenderId == id));
        db.OtpCodes.RemoveRange(db.OtpCodes.Where(o => o.UserId == id));
        await db.SaveChangesAsync();
        await users.DeleteAsync(u);
        return NoContent();
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts() =>
        Ok(await (from a in db.Alerts join u in db.Users on a.ChildId equals u.Id orderby a.Id descending
                  select new { a.Id, a.Type, a.Message, a.IsResolved, a.CreatedAt, a.FamilyId, childName = u.FullName })
            .Take(100).ToListAsync());
}
