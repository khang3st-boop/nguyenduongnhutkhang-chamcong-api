using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;
using NguyenDuongNhutKhang_app.Services;

namespace NguyenDuongNhutKhang_app.Controllers;

public record ScanDto(
    [Required] string Payload,
    [Range(-90, 90)] double Latitude,
    [Range(-180, 180)] double Longitude);

public record AttendanceEditDto(DateTime CheckIn, DateTime? CheckOut, [StringLength(300)] string? Note);
public record AttendanceCreateDto([Required] string UserId, int WorkplaceId, DateTime CheckIn, DateTime? CheckOut, [StringLength(300)] string? Note);

/// <summary>
/// Chấm công bằng quét QR xoay vòng + kiểm tra GPS trong bán kính địa điểm.
/// Quét lần đầu trong ngày = vào ca, quét tiếp = ra ca. Quản lý xem, sửa, xóa và xuất báo cáo tháng.
/// </summary>
[Route("api/[controller]")]
public class AttendanceController(AppDbContext db, QrService qr) : ApiBase
{
    // Giờ Việt Nam (UTC+7) dùng để tính ngày công, đi trễ, về sớm.
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);
    private static DateTime Local(DateTime utc) => utc + Vn;
    private static int MinuteOfDay(DateTime utc) => (int)Local(utc).TimeOfDay.TotalMinutes;

    private static object View(Attendance a, string? name = null, string? place = null) => new
    {
        a.Id, a.UserId, userName = name, a.WorkplaceId, workplace = place, a.CheckIn, a.CheckOut, a.Status,
        a.LateMinutes, a.EarlyLeaveMinutes, a.Note, a.InLat, a.InLng, a.OutLat, a.OutLng
    };

    [Authorize(Roles = Roles.Employee), HttpPost("scan")]
    public async Task<IActionResult> Scan(ScanDto dto)
    {
        var wid = QrService.ParseWorkplaceId(dto.Payload);
        if (wid == null) return Fail(400, "Mã QR không phải của hệ thống chấm công.");
        var z = await db.Workplaces.FirstOrDefaultAsync(x => x.Id == wid && x.CompanyId == CompanyId);
        if (z == null) return Fail(400, "Mã QR không thuộc công ty của bạn.");
        if (!qr.Verify(z, dto.Payload)) return Fail(400, "Mã QR đã hết hạn hoặc không hợp lệ. Hãy quét lại mã đang hiển thị.");

        var dist = Geo.DistanceMeters(dto.Latitude, dto.Longitude, z.Latitude, z.Longitude);
        if (dist > z.RadiusMeters)
            return Fail(403, $"Bạn đang cách \"{z.Name}\" khoảng {dist:0} m, ngoài phạm vi chấm công ({z.RadiusMeters:0} m).");

        var now = DateTime.UtcNow;
        var open = await OpenRecord(UserId);
        if (open == null)
        {
            var mod = MinuteOfDay(now);
            // Chỉ cho vào ca trong khung giờ làm việc: sớm nhất 2 giờ trước giờ vào ca, đến hết giờ tan ca.
            if (z.EndMinute > z.StartMinute)
            {
                static string Hm(int m) => $"{m / 60:00}:{m % 60:00}";
                var earliest = Math.Max(0, z.StartMinute - 120);
                if (mod < earliest)
                    return Fail(400, $"Chưa đến giờ chấm công. Ca làm {Hm(z.StartMinute)}–{Hm(z.EndMinute)}, chấm công sớm nhất từ {Hm(earliest)}.");
                if (mod >= z.EndMinute)
                    return Fail(400, $"Đã hết giờ làm ({Hm(z.StartMinute)}–{Hm(z.EndMinute)}), không thể vào ca lúc này.");
            }
            var late = Math.Max(0, mod - z.StartMinute);
            var isLate = late > z.GraceMinutes;
            var a = new Attendance
            {
                CompanyId = CompanyId, UserId = UserId, WorkplaceId = z.Id, CheckIn = now, InLat = dto.Latitude, InLng = dto.Longitude,
                Status = isLate ? "Late" : "OnTime", LateMinutes = isLate ? late : 0
            };
            db.Attendances.Add(a);
            await db.SaveChangesAsync();
            return Ok(new { status = true, action = "in", time = now, late = isLate, lateMinutes = a.LateMinutes, workplace = z.Name, distance = Math.Round(dist) });
        }
        if (now - open.CheckIn < TimeSpan.FromMinutes(1)) return Fail(429, "Bạn vừa chấm công xong, vui lòng đợi 1 phút.");
        open.CheckOut = now; open.OutLat = dto.Latitude; open.OutLng = dto.Longitude;
        open.EarlyLeaveMinutes = Math.Max(0, z.EndMinute - MinuteOfDay(now));
        await db.SaveChangesAsync();
        return Ok(new
        {
            status = true, action = "out", time = now, early = open.EarlyLeaveMinutes > 0, earlyMinutes = open.EarlyLeaveMinutes,
            workedMinutes = (int)(now - open.CheckIn).TotalMinutes, workplace = z.Name, distance = Math.Round(dist)
        });
    }

    /// <summary>Trạng thái hôm nay của nhân viên: đang trong ca hay chưa.</summary>
    [Authorize(Roles = Roles.Employee), HttpGet("today")]
    public async Task<IActionResult> Today()
    {
        var open = await OpenRecord(UserId);
        var startLocalDay = Local(DateTime.UtcNow).Date - Vn;
        var done = await db.Attendances.Where(a => a.UserId == UserId && a.CheckIn >= startLocalDay && a.CheckOut != null)
            .OrderByDescending(a => a.Id).FirstOrDefaultAsync();
        return Ok(new { working = open != null, open = open == null ? null : View(open), last = done == null ? null : View(done) });
    }

    [Authorize(Roles = Roles.Employee), HttpGet("mine")]
    public async Task<IActionResult> Mine([FromQuery] string? month)
    {
        var (from, to) = MonthRange(month);
        var rows = await db.Attendances.Where(a => a.UserId == UserId && a.CheckIn >= from && a.CheckIn < to)
            .OrderByDescending(a => a.CheckIn).ToListAsync();
        var places = await PlaceNames();
        return Ok(rows.Select(a => View(a, null, places.GetValueOrDefault(a.WorkplaceId))));
    }

    [Authorize(Roles = Roles.Manager), HttpGet]
    public async Task<IActionResult> List([FromQuery] string? month, [FromQuery] string? userId)
    {
        var (from, to) = MonthRange(month);
        var q = db.Attendances.Where(a => a.CompanyId == CompanyId && a.CheckIn >= from && a.CheckIn < to);
        if (!string.IsNullOrEmpty(userId)) q = q.Where(a => a.UserId == userId);
        var rows = await q.OrderByDescending(a => a.CheckIn).Take(1000).ToListAsync();
        var names = await db.Users.Where(u => u.CompanyId == CompanyId).ToDictionaryAsync(u => u.Id, u => u.FullName);
        var places = await PlaceNames();
        return Ok(rows.Select(a => View(a, names.GetValueOrDefault(a.UserId), places.GetValueOrDefault(a.WorkplaceId))));
    }

    /// <summary>Báo cáo tháng: số ngày công, số lần đi trễ, tổng giờ làm của từng nhân viên.</summary>
    [Authorize(Roles = Roles.Manager), HttpGet("report")]
    public async Task<IActionResult> Report([FromQuery] string? month)
    {
        var (from, to) = MonthRange(month);
        var rows = await db.Attendances.Where(a => a.CompanyId == CompanyId && a.CheckIn >= from && a.CheckIn < to).ToListAsync();
        var names = await db.Users.Where(u => u.CompanyId == CompanyId).ToDictionaryAsync(u => u.Id, u => u.FullName);
        var report = rows.GroupBy(a => a.UserId).Select(g => new
        {
            userId = g.Key,
            fullName = names.GetValueOrDefault(g.Key, "(đã xóa)"),
            days = g.Select(a => Local(a.CheckIn).Date).Distinct().Count(),
            lateCount = g.Count(a => a.Status == "Late"),
            lateMinutes = g.Sum(a => a.LateMinutes),
            earlyLeaveCount = g.Count(a => a.EarlyLeaveMinutes > 0),
            hours = Math.Round(g.Where(a => a.CheckOut != null).Sum(a => (a.CheckOut!.Value - a.CheckIn).TotalHours), 1),
            missingCheckout = g.Count(a => a.CheckOut == null)
        }).OrderBy(x => x.fullName).ToList();
        return Ok(new { month = Local(from).ToString("yyyy-MM"), employees = report });
    }

    /// <summary>Danh sách đang trong ca ngay lúc này (bảng điều khiển của quản lý).</summary>
    [Authorize(Roles = Roles.Manager), HttpGet("live")]
    public async Task<IActionResult> Live()
    {
        var since = DateTime.UtcNow.AddHours(-20);
        var rows = await db.Attendances.Where(a => a.CompanyId == CompanyId && a.CheckOut == null && a.CheckIn >= since).ToListAsync();
        var names = await db.Users.Where(u => u.CompanyId == CompanyId).ToDictionaryAsync(u => u.Id, u => u.FullName);
        var startLocalDay = Local(DateTime.UtcNow).Date - Vn;
        var todayCount = await db.Attendances.Where(a => a.CompanyId == CompanyId && a.CheckIn >= startLocalDay).Select(a => a.UserId).Distinct().CountAsync();
        var total = await (from u in db.Users join ur in db.UserRoles on u.Id equals ur.UserId join r in db.Roles on ur.RoleId equals r.Id
                           where u.CompanyId == CompanyId && r.Name == Roles.Employee select u.Id).CountAsync();
        var lateToday = await db.Attendances.CountAsync(a => a.CompanyId == CompanyId && a.CheckIn >= startLocalDay && a.Status == "Late");
        return Ok(new
        {
            totalEmployees = total, presentToday = todayCount, lateToday, working = rows.Count,
            current = rows.Select(a => new { a.UserId, fullName = names.GetValueOrDefault(a.UserId), a.CheckIn, a.Status })
        });
    }

    [Authorize(Roles = Roles.Manager), HttpPost]
    public async Task<IActionResult> Create(AttendanceCreateDto dto)
    {
        if (!await db.Users.AnyAsync(u => u.Id == dto.UserId && u.CompanyId == CompanyId)) return Fail(400, "Nhân viên không thuộc công ty.");
        var z = await db.Workplaces.FirstOrDefaultAsync(x => x.Id == dto.WorkplaceId && x.CompanyId == CompanyId);
        if (z == null) return Fail(400, "Địa điểm không hợp lệ.");
        if (dto.CheckOut != null && dto.CheckOut < dto.CheckIn) return Fail(400, "Giờ ra phải sau giờ vào.");
        var a = new Attendance { CompanyId = CompanyId, UserId = dto.UserId, WorkplaceId = z.Id, Note = dto.Note };
        Apply(a, z, dto.CheckIn, dto.CheckOut);
        db.Attendances.Add(a);
        await db.SaveChangesAsync();
        return Ok(View(a));
    }

    [Authorize(Roles = Roles.Manager), HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, AttendanceEditDto dto)
    {
        var a = await db.Attendances.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        if (a == null) return NotFound();
        if (dto.CheckOut != null && dto.CheckOut < dto.CheckIn) return Fail(400, "Giờ ra phải sau giờ vào.");
        var z = await db.Workplaces.FirstAsync(x => x.Id == a.WorkplaceId);
        a.Note = dto.Note;
        Apply(a, z, dto.CheckIn, dto.CheckOut);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = Roles.Manager), HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var a = await db.Attendances.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId);
        if (a == null) return NotFound();
        db.Attendances.Remove(a);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static void Apply(Attendance a, Workplace z, DateTime checkIn, DateTime? checkOut)
    {
        a.CheckIn = DateTime.SpecifyKind(checkIn, DateTimeKind.Utc);
        a.CheckOut = checkOut == null ? null : DateTime.SpecifyKind(checkOut.Value, DateTimeKind.Utc);
        var late = Math.Max(0, MinuteOfDay(a.CheckIn) - z.StartMinute);
        a.Status = late > z.GraceMinutes ? "Late" : "OnTime";
        a.LateMinutes = late > z.GraceMinutes ? late : 0;
        a.EarlyLeaveMinutes = a.CheckOut == null ? 0 : Math.Max(0, z.EndMinute - MinuteOfDay(a.CheckOut.Value));
    }

    private Task<Attendance?> OpenRecord(string userId)
    {
        var since = DateTime.UtcNow.AddHours(-20); // ca chưa ra quá 20 giờ coi như quên chấm ra, bắt đầu ca mới
        return db.Attendances.Where(a => a.UserId == userId && a.CheckOut == null && a.CheckIn >= since)
            .OrderByDescending(a => a.Id).FirstOrDefaultAsync();
    }

    private Task<Dictionary<int, string>> PlaceNames() =>
        db.Workplaces.Where(z => z.CompanyId == CompanyId).ToDictionaryAsync(z => z.Id, z => z.Name);

    /// <summary>Khoảng UTC tương ứng một tháng theo giờ Việt Nam (mặc định tháng hiện tại).</summary>
    private static (DateTime from, DateTime to) MonthRange(string? month)
    {
        var start = DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)
            ? new DateTime(m.Year, m.Month, 1)
            : new DateTime(Local(DateTime.UtcNow).Year, Local(DateTime.UtcNow).Month, 1);
        // Npgsql chỉ nhận DateTime Kind=Utc cho cột timestamptz; để Unspecified sẽ ném lỗi 500.
        return (DateTime.SpecifyKind(start - Vn, DateTimeKind.Utc), DateTime.SpecifyKind(start.AddMonths(1) - Vn, DateTimeKind.Utc));
    }
}
