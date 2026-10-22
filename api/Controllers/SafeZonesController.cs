using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;

namespace NguyenDuongNhutKhang_app.Controllers;

public record ZoneDto(
    [Required, StringLength(120)] string Name,
    [Range(-90, 90)] double Latitude,
    [Range(-180, 180)] double Longitude,
    [Range(30, 50000)] double RadiusMeters,
    bool NotifyEnter = true,
    bool NotifyExit = true);

/// <summary>CRUD vùng an toàn. Phụ huynh ghi; phụ huynh và trẻ đọc.</summary>
[Authorize(Roles = $"{Roles.Parent},{Roles.Child}"), Route("api/[controller]")]
public class SafeZonesController(AppDbContext db) : ApiBase
{
    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await db.SafeZones.Where(z => z.FamilyId == FamilyId).OrderBy(z => z.Name).ToListAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var z = await db.SafeZones.FirstOrDefaultAsync(x => x.Id == id && x.FamilyId == FamilyId);
        return z == null ? NotFound() : Ok(z);
    }

    [Authorize(Roles = Roles.Parent), HttpPost]
    public async Task<IActionResult> Create(ZoneDto dto)
    {
        var z = new SafeZone { FamilyId = FamilyId, Name = dto.Name.Trim(), Latitude = dto.Latitude, Longitude = dto.Longitude, RadiusMeters = dto.RadiusMeters, NotifyEnter = dto.NotifyEnter, NotifyExit = dto.NotifyExit };
        db.SafeZones.Add(z);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = z.Id }, z);
    }

    [Authorize(Roles = Roles.Parent), HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ZoneDto dto)
    {
        var z = await db.SafeZones.FirstOrDefaultAsync(x => x.Id == id && x.FamilyId == FamilyId);
        if (z == null) return NotFound();
        z.Name = dto.Name.Trim(); z.Latitude = dto.Latitude; z.Longitude = dto.Longitude; z.RadiusMeters = dto.RadiusMeters; z.NotifyEnter = dto.NotifyEnter; z.NotifyExit = dto.NotifyExit;
        db.ZoneStates.RemoveRange(db.ZoneStates.Where(s => s.ZoneId == id)); // tính lại trạng thái theo ranh giới mới
        await db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = Roles.Parent), HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var z = await db.SafeZones.FirstOrDefaultAsync(x => x.Id == id && x.FamilyId == FamilyId);
        if (z == null) return NotFound();
        db.ZoneStates.RemoveRange(db.ZoneStates.Where(s => s.ZoneId == id));
        db.SafeZones.Remove(z);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
