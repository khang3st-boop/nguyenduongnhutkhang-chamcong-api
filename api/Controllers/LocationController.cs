using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;

namespace NguyenDuongNhutKhang_app.Controllers;

public record LocationDto([Range(-90, 90)] double Latitude, [Range(-180, 180)] double Longitude, double? Accuracy, [Range(0, 100)] int? Battery);

/// <summary>Chia sẻ vị trí: nhân viên đẩy vị trí khi đang làm việc, quản lý xem gần thời gian thực trên bản đồ.</summary>
[Route("api/[controller]")]
public class LocationController(AppDbContext db) : ApiBase
{
    [Authorize(Roles = Roles.Employee), HttpPost]
    public async Task<IActionResult> Post(LocationDto dto)
    {
        db.Locations.Add(new LocationPoint
        {
            UserId = UserId, Latitude = dto.Latitude, Longitude = dto.Longitude, Accuracy = dto.Accuracy, Battery = dto.Battery
        });
        // Giữ bảng gọn: xóa điểm cũ hơn 7 ngày của chính nhân viên này.
        var old = DateTime.UtcNow.AddDays(-7);
        db.Locations.RemoveRange(db.Locations.Where(l => l.UserId == UserId && l.CreatedAt < old));
        await db.SaveChangesAsync();
        return Ok(new { status = true });
    }

    [Authorize(Roles = Roles.Manager), HttpGet("team")]
    public async Task<IActionResult> Team()
    {
        var ids = await (from u in db.Users
                         join ur in db.UserRoles on u.Id equals ur.UserId
                         join r in db.Roles on ur.RoleId equals r.Id
                         where u.CompanyId == CompanyId && r.Name == Roles.Employee
                         select u.Id).ToListAsync();
        var last = await db.Locations.Where(l => ids.Contains(l.UserId))
            .GroupBy(l => l.UserId).Select(g => g.OrderByDescending(x => x.Id).First()).ToListAsync();
        var names = await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        return Ok(last.Select(l => new
        {
            employeeId = l.UserId, employeeName = names[l.UserId], l.Latitude, l.Longitude, l.Accuracy, l.Battery, l.CreatedAt
        }));
    }

    [Authorize(Roles = Roles.Manager), HttpGet("history/{employeeId}")]
    public async Task<IActionResult> History(string employeeId, [FromQuery] int hours = 24)
    {
        if (!await db.Users.AnyAsync(u => u.Id == employeeId && u.CompanyId == CompanyId)) return NotFound();
        var since = DateTime.UtcNow.AddHours(-Math.Clamp(hours, 1, 168));
        return Ok(await db.Locations.Where(l => l.UserId == employeeId && l.CreatedAt >= since)
            .OrderBy(l => l.Id).Select(l => new { l.Latitude, l.Longitude, l.Battery, l.CreatedAt }).Take(2000).ToListAsync());
    }
}
