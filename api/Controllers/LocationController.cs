using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;
using NguyenDuongNhutKhang_app.Services;

namespace NguyenDuongNhutKhang_app.Controllers;

public record LocationDto([Range(-90, 90)] double Latitude, [Range(-180, 180)] double Longitude, double? Accuracy, [Range(0, 100)] int? Battery);

/// <summary>Chia sẻ vị trí: trẻ đẩy vị trí, phụ huynh đọc gần thời gian thực. Server kiểm tra vùng an toàn mỗi lần nhận.</summary>
[Route("api/[controller]")]
public class LocationController(AppDbContext db) : ApiBase
{
    [Authorize(Roles = Roles.Child), HttpPost]
    public async Task<IActionResult> Post(LocationDto dto)
    {
        db.Locations.Add(new LocationPoint
        {
            UserId = UserId, Latitude = dto.Latitude, Longitude = dto.Longitude, Accuracy = dto.Accuracy, Battery = dto.Battery
        });

        var name = await db.Users.Where(u => u.Id == UserId).Select(u => u.FullName).FirstAsync();
        var zones = await db.SafeZones.Where(z => z.FamilyId == FamilyId).ToListAsync();
        var states = await db.ZoneStates.Where(s => s.UserId == UserId).ToListAsync();
        var events = new List<object>();
        foreach (var z in zones)
        {
            var dist = Geo.DistanceMeters(dto.Latitude, dto.Longitude, z.Latitude, z.Longitude);
            var inside = dist <= z.RadiusMeters;
            var st = states.FirstOrDefault(s => s.ZoneId == z.Id);
            if (st == null)
            {
                db.ZoneStates.Add(new ZoneState { UserId = UserId, ZoneId = z.Id, Inside = inside });
                continue;
            }
            if (st.Inside == inside) continue;
            st.Inside = inside;
            var type = inside ? "EnterZone" : "ExitZone";
            var msg = inside ? $"{name} đã vào vùng \"{z.Name}\"." : $"{name} đã rời khỏi vùng an toàn \"{z.Name}\" ({dist:0} m từ tâm).";
            db.Alerts.Add(new Alert
            {
                FamilyId = FamilyId, ChildId = UserId, Type = type, Message = msg,
                Latitude = dto.Latitude, Longitude = dto.Longitude, Battery = dto.Battery
            });
            events.Add(new { type, zone = z.Name });
        }
        await db.SaveChangesAsync();
        return Ok(new { status = true, events });
    }

    [Authorize(Roles = Roles.Parent), HttpGet("family")]
    public async Task<IActionResult> Family()
    {
        var kidIds = await (from u in db.Users
                            join ur in db.UserRoles on u.Id equals ur.UserId
                            join r in db.Roles on ur.RoleId equals r.Id
                            where u.FamilyId == FamilyId && r.Name == Roles.Child
                            select u.Id).ToListAsync();
        var last = await db.Locations.Where(l => kidIds.Contains(l.UserId))
            .GroupBy(l => l.UserId).Select(g => g.OrderByDescending(x => x.Id).First()).ToListAsync();
        var names = await db.Users.Where(u => kidIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        return Ok(last.Select(l => new
        {
            childId = l.UserId, childName = names[l.UserId], l.Latitude, l.Longitude, l.Accuracy, l.Battery, l.CreatedAt
        }));
    }

    [Authorize(Roles = Roles.Parent), HttpGet("history/{childId}")]
    public async Task<IActionResult> History(string childId, [FromQuery] int hours = 24)
    {
        if (!await db.Users.AnyAsync(u => u.Id == childId && u.FamilyId == FamilyId)) return NotFound();
        var since = DateTime.UtcNow.AddHours(-Math.Clamp(hours, 1, 168));
        return Ok(await db.Locations.Where(l => l.UserId == childId && l.CreatedAt >= since)
            .OrderBy(l => l.Id).Select(l => new { l.Latitude, l.Longitude, l.Battery, l.CreatedAt }).Take(2000).ToListAsync());
    }
}
