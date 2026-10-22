using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;
using NguyenDuongNhutKhang_app.Services;

namespace NguyenDuongNhutKhang_app.Controllers;

public record WorkplaceDto(
    [Required, StringLength(120)] string Name,
    [Range(-90, 90)] double Latitude,
    [Range(-180, 180)] double Longitude,
    [Range(30, 50000)] double RadiusMeters,
    [Range(0, 1439)] int StartMinute = 480,
    [Range(0, 1439)] int EndMinute = 1020,
    [Range(0, 240)] int GraceMinutes = 10);

/// <summary>CRUD địa điểm làm việc. Quản lý ghi; quản lý và nhân viên đọc. Quản lý lấy mã QR xoay vòng 30 giây để nhân viên quét.</summary>
[Authorize(Roles = $"{Roles.Manager},{Roles.Employee}"), Route("api/[controller]")]
public class WorkplacesController(AppDbContext db, QrService qr) : ApiBase
{
    private static object View(Workplace z) => new
    {
        z.Id, z.Name, z.Latitude, z.Longitude, z.RadiusMeters, z.StartMinute, z.EndMinute, z.GraceMinutes
    };

    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok((await db.Workplaces.Where(z => z.CompanyId == CompanyId).OrderBy(z => z.Name).ToListAsync()).Select(View));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var z = await db.Workplaces.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        return z == null ? NotFound() : Ok(View(z));
    }

    [Authorize(Roles = Roles.Manager), HttpPost]
    public async Task<IActionResult> Create(WorkplaceDto dto)
    {
        var z = new Workplace { CompanyId = CompanyId, QrSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) };
        Apply(z, dto);
        db.Workplaces.Add(z);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = z.Id }, View(z));
    }

    [Authorize(Roles = Roles.Manager), HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, WorkplaceDto dto)
    {
        var z = await db.Workplaces.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        if (z == null) return NotFound();
        Apply(z, dto);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = Roles.Manager), HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var z = await db.Workplaces.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        if (z == null) return NotFound();
        if (await db.Attendances.AnyAsync(a => a.WorkplaceId == id))
            return Fail(400, "Địa điểm đã có dữ liệu chấm công, không thể xóa.");
        db.Workplaces.Remove(z);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Mã QR hiện tại của địa điểm (đổi mỗi 30 giây, chống chụp ảnh gửi người khác chấm hộ).</summary>
    [Authorize(Roles = Roles.Manager), HttpGet("{id:int}/qr")]
    public async Task<IActionResult> Qr(int id)
    {
        var z = await db.Workplaces.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        if (z == null) return NotFound();
        var (payload, secondsLeft) = qr.Create(z);
        return Ok(new { payload, secondsLeft, windowSeconds = QrService.WindowSeconds, workplace = z.Name });
    }

    private static void Apply(Workplace z, WorkplaceDto d)
    {
        z.Name = d.Name.Trim(); z.Latitude = d.Latitude; z.Longitude = d.Longitude; z.RadiusMeters = d.RadiusMeters;
        z.StartMinute = d.StartMinute; z.EndMinute = d.EndMinute; z.GraceMinutes = d.GraceMinutes;
    }
}
