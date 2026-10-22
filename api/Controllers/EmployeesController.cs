using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;

namespace NguyenDuongNhutKhang_app.Controllers;

public record EmployeeCreateDto(
    [Required, StringLength(40, MinimumLength = 3), RegularExpression(@"^[A-Za-z0-9_.]+$")] string Username,
    [Required, MinLength(6)] string Password,
    [Required, StringLength(120)] string FullName,
    [EmailAddress] string? Email,
    [StringLength(80)] string? Position = null,
    [StringLength(80)] string? Department = null);

public record EmployeeUpdateDto(
    [Required, StringLength(120)] string FullName,
    [MinLength(6)] string? Password,
    [EmailAddress] string? Email,
    [StringLength(80)] string? Position = null,
    [StringLength(80)] string? Department = null);

/// <summary>CRUD nhân viên của công ty (chỉ quản lý).</summary>
[Authorize(Roles = Roles.Manager), Route("api/[controller]")]
public class EmployeesController(UserManager<AppUser> users, AppDbContext db) : ApiBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var emps = await Employees().OrderBy(e => e.FullName).ToListAsync();
        var ids = emps.Select(k => k.Id).ToList();
        var last = await db.Locations.Where(l => ids.Contains(l.UserId))
            .GroupBy(l => l.UserId).Select(g => g.OrderByDescending(x => x.Id).First()).ToListAsync();
        var since = DateTime.UtcNow.AddHours(-20);
        var open = await db.Attendances.Where(a => ids.Contains(a.UserId) && a.CheckOut == null && a.CheckIn >= since)
            .Select(a => a.UserId).Distinct().ToListAsync();
        return Ok(emps.Select(k =>
        {
            var l = last.FirstOrDefault(x => x.UserId == k.Id);
            return new
            {
                k.Id, userName = k.UserName, k.FullName, k.Email, k.Position, k.Department, k.CreatedAt,
                working = open.Contains(k.Id),
                lastLocation = l == null ? null : new { l.Latitude, l.Longitude, l.Accuracy, l.Battery, l.CreatedAt }
            };
        }));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var k = await Employees().FirstOrDefaultAsync(x => x.Id == id);
        return k == null ? NotFound() : Ok(new { k.Id, userName = k.UserName, k.FullName, k.Email, k.Position, k.Department, k.CreatedAt });
    }

    [HttpPost]
    public async Task<IActionResult> Create(EmployeeCreateDto dto)
    {
        if (await users.FindByNameAsync(dto.Username) != null) return Fail(400, "Tên đăng nhập đã tồn tại.");
        var emp = new AppUser
        {
            UserName = dto.Username, FullName = dto.FullName.Trim(), Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email,
            EmailConfirmed = true, CompanyId = CompanyId, Position = dto.Position, Department = dto.Department
        };
        var r = await users.CreateAsync(emp, dto.Password);
        if (!r.Succeeded) return BadRequest(new { status = false, message = string.Join(" ", r.Errors.Select(e => e.Description)) });
        await users.AddToRoleAsync(emp, Roles.Employee);
        return CreatedAtAction(nameof(Get), new { id = emp.Id }, new { emp.Id, userName = emp.UserName, emp.FullName });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, EmployeeUpdateDto dto)
    {
        var emp = await Employees().FirstOrDefaultAsync(x => x.Id == id);
        if (emp == null) return NotFound();
        emp.FullName = dto.FullName.Trim();
        emp.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email;
        emp.Position = dto.Position; emp.Department = dto.Department;
        var r = await users.UpdateAsync(emp);
        if (r.Succeeded && !string.IsNullOrEmpty(dto.Password))
        {
            var token = await users.GeneratePasswordResetTokenAsync(emp);
            r = await users.ResetPasswordAsync(emp, token, dto.Password);
        }
        return r.Succeeded ? NoContent()
            : BadRequest(new { status = false, message = string.Join(" ", r.Errors.Select(e => e.Description)) });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var emp = await Employees().FirstOrDefaultAsync(x => x.Id == id);
        if (emp == null) return NotFound();
        db.Locations.RemoveRange(db.Locations.Where(l => l.UserId == id));
        db.Attendances.RemoveRange(db.Attendances.Where(a => a.UserId == id));
        db.Messages.RemoveRange(db.Messages.Where(m => m.SenderId == id));
        db.OtpCodes.RemoveRange(db.OtpCodes.Where(o => o.UserId == id));
        await db.SaveChangesAsync();
        await users.DeleteAsync(emp);
        return NoContent();
    }

    private IQueryable<AppUser> Employees() =>
        from u in db.Users
        join ur in db.UserRoles on u.Id equals ur.UserId
        join r in db.Roles on ur.RoleId equals r.Id
        where u.CompanyId == CompanyId && r.Name == Roles.Employee
        select u;
}
