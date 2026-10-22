using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;
using NguyenDuongNhutKhang_app.Services;

namespace NguyenDuongNhutKhang_app.Controllers;

public class SosForm
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? Battery { get; set; }
    public string? Note { get; set; }
    public IFormFile? Photo { get; set; }
}

/// <summary>Cảnh báo (SOS + ra/vào vùng an toàn). SOS thông minh: tự đính kèm vị trí, pin, vùng gần nhất và ảnh hiện trường.</summary>
[Route("api/[controller]")]
public class AlertsController(AppDbContext db, IWebHostEnvironment env) : ApiBase
{
    private static readonly string[] AllowedExt = [".jpg", ".jpeg", ".png", ".webp"];

    [Authorize(Roles = Roles.Child), HttpPost("sos"), Consumes("multipart/form-data")]
    public async Task<IActionResult> Sos([FromForm] SosForm form)
    {
        var me = await db.Users.FirstAsync(u => u.Id == UserId);
        var lat = form.Latitude;
        var lng = form.Longitude;
        int? battery = form.Battery;
        if (lat == null || lng == null)
        {
            var last = await db.Locations.Where(l => l.UserId == UserId).OrderByDescending(l => l.Id).FirstOrDefaultAsync();
            if (last != null) { lat = last.Latitude; lng = last.Longitude; battery ??= last.Battery; }
        }

        var msg = $"🆘 {me.FullName} đang cầu cứu!";
        if (lat != null && lng != null)
        {
            var zones = await db.SafeZones.Where(z => z.FamilyId == FamilyId).ToListAsync();
            var nearest = zones.Select(z => (z, d: Geo.DistanceMeters(lat.Value, lng.Value, z.Latitude, z.Longitude)))
                .OrderBy(x => x.d).FirstOrDefault();
            if (nearest.z != null)
                msg += nearest.d <= nearest.z.RadiusMeters
                    ? $" Đang trong vùng \"{nearest.z.Name}\"."
                    : $" Cách \"{nearest.z.Name}\" khoảng {nearest.d:0} m (ngoài vùng an toàn).";
        }
        else msg += " Chưa có thông tin vị trí.";
        if (battery != null) msg += $" Pin {battery}%.";
        if (!string.IsNullOrWhiteSpace(form.Note)) msg += $" Ghi chú: {form.Note.Trim()[..Math.Min(200, form.Note.Trim().Length)]}";

        string? imageUrl = null;
        if (form.Photo is { Length: > 0 })
        {
            var ext = Path.GetExtension(form.Photo.FileName).ToLowerInvariant();
            if (!AllowedExt.Contains(ext) || form.Photo.Length > 5_000_000) return Fail(400, "Ảnh phải là jpg/png/webp và nhỏ hơn 5 MB.");
            var dir = Path.Combine(env.WebRootPath, "uploads");
            Directory.CreateDirectory(dir);
            var file = $"{Guid.NewGuid():N}{ext}";
            await using (var fs = System.IO.File.Create(Path.Combine(dir, file))) await form.Photo.CopyToAsync(fs);
            imageUrl = $"/uploads/{file}";
        }

        // Gộp các lần bấm liên tiếp trong 30 giây thành một cảnh báo, tránh spam phụ huynh.
        var since = DateTime.UtcNow.AddSeconds(-30);
        var existing = await db.Alerts.FirstOrDefaultAsync(a => a.ChildId == UserId && a.Type == "SOS" && !a.IsResolved && a.CreatedAt >= since);
        if (existing != null)
        {
            existing.Message = msg; existing.Latitude = lat; existing.Longitude = lng; existing.Battery = battery;
            existing.ImageUrl = imageUrl ?? existing.ImageUrl;
            await db.SaveChangesAsync();
            return Ok(new { status = true, id = existing.Id, merged = true });
        }
        var alert = new Alert
        {
            FamilyId = FamilyId, ChildId = UserId, Type = "SOS", Message = msg,
            Latitude = lat, Longitude = lng, Battery = battery, ImageUrl = imageUrl
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        return Ok(new { status = true, id = alert.Id, merged = false });
    }

    [Authorize(Roles = Roles.Parent), HttpGet]
    public async Task<IActionResult> List([FromQuery] bool unresolvedOnly = false, [FromQuery] int afterId = 0)
    {
        var q = db.Alerts.Where(a => a.FamilyId == FamilyId && a.Id > afterId);
        if (unresolvedOnly) q = q.Where(a => !a.IsResolved);
        var rows = await (from a in q
                          join u in db.Users on a.ChildId equals u.Id
                          orderby a.Id descending
                          select new
                          {
                              a.Id, a.Type, a.Message, a.Latitude, a.Longitude, a.Battery, a.ImageUrl, a.IsResolved, a.CreatedAt,
                              childId = a.ChildId, childName = u.FullName
                          }).Take(200).ToListAsync();
        return Ok(rows);
    }

    [Authorize(Roles = Roles.Parent), HttpPut("{id:int}/resolve")]
    public async Task<IActionResult> Resolve(int id)
    {
        var a = await db.Alerts.FirstOrDefaultAsync(x => x.Id == id && x.FamilyId == FamilyId);
        if (a == null) return NotFound();
        a.IsResolved = true;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = Roles.Parent), HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var a = await db.Alerts.FirstOrDefaultAsync(x => x.Id == id && x.FamilyId == FamilyId);
        if (a == null) return NotFound();
        db.Alerts.Remove(a);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
