using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;

namespace NguyenDuongNhutKhang_app.Controllers;

public record ChildCreateDto(
    [Required, StringLength(40, MinimumLength = 3), RegularExpression(@"^[A-Za-z0-9_.]+$")] string Username,
    [Required, MinLength(6)] string Password,
    [Required, StringLength(120)] string FullName,
    [EmailAddress] string? Email,
    DateOnly? BirthDate = null,
    [RegularExpression("^(male|female|unspecified)$")] string? Gender = null);

public record ChildUpdateDto([Required, StringLength(120)] string FullName, [MinLength(6)] string? Password, [EmailAddress] string? Email,
    DateOnly? BirthDate = null,
    [RegularExpression("^(male|female|unspecified)$")] string? Gender = null);

/// <summary>CRUD hồ sơ trẻ em của gia đình (chỉ phụ huynh).</summary>
[Authorize(Roles = Roles.Parent), Route("api/[controller]")]
public class ChildrenController(UserManager<AppUser> users, AppDbContext db) : ApiBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var kids = await Kids().ToListAsync();
        var ids = kids.Select(k => k.Id).ToList();
        var last = await db.Locations.Where(l => ids.Contains(l.UserId))
            .GroupBy(l => l.UserId).Select(g => g.OrderByDescending(x => x.Id).First()).ToListAsync();
        return Ok(kids.Select(k =>
        {
            var l = last.FirstOrDefault(x => x.UserId == k.Id);
            return new
            {
                k.Id, userName = k.UserName, k.FullName, k.Email, k.AvatarUrl, k.CreatedAt, k.BirthDate, k.Gender,
                lastLocation = l == null ? null : new { l.Latitude, l.Longitude, l.Accuracy, l.Battery, l.CreatedAt }
            };
        }));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var k = await Kids().FirstOrDefaultAsync(x => x.Id == id);
        return k == null ? NotFound() : Ok(new { k.Id, userName = k.UserName, k.FullName, k.Email, k.AvatarUrl, k.CreatedAt, k.BirthDate, k.Gender });
    }

    [HttpPost]
    public async Task<IActionResult> Create(ChildCreateDto dto)
    {
        if (await users.FindByNameAsync(dto.Username) != null) return Fail(400, "Tên đăng nhập đã tồn tại.");
        var kid = new AppUser
        {
            UserName = dto.Username, FullName = dto.FullName.Trim(), Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email,
            EmailConfirmed = true, FamilyId = FamilyId, BirthDate = dto.BirthDate, Gender = dto.Gender
        };
        var r = await users.CreateAsync(kid, dto.Password);
        if (!r.Succeeded) return BadRequest(new { status = false, message = string.Join(" ", r.Errors.Select(e => e.Description)) });
        await users.AddToRoleAsync(kid, Roles.Child);
        return CreatedAtAction(nameof(Get), new { id = kid.Id }, new { kid.Id, userName = kid.UserName, kid.FullName });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, ChildUpdateDto dto)
    {
        var kid = await Kids().FirstOrDefaultAsync(x => x.Id == id);
        if (kid == null) return NotFound();
        kid.FullName = dto.FullName.Trim();
        kid.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email;
        kid.BirthDate = dto.BirthDate; kid.Gender = dto.Gender;
        var r = await users.UpdateAsync(kid);
        if (r.Succeeded && !string.IsNullOrEmpty(dto.Password))
        {
            var token = await users.GeneratePasswordResetTokenAsync(kid);
            r = await users.ResetPasswordAsync(kid, token, dto.Password);
        }
        return r.Succeeded ? NoContent()
            : BadRequest(new { status = false, message = string.Join(" ", r.Errors.Select(e => e.Description)) });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var kid = await Kids().FirstOrDefaultAsync(x => x.Id == id);
        if (kid == null) return NotFound();
        db.Locations.RemoveRange(db.Locations.Where(l => l.UserId == id));
        db.ZoneStates.RemoveRange(db.ZoneStates.Where(s => s.UserId == id));
        db.Alerts.RemoveRange(db.Alerts.Where(a => a.ChildId == id));
        db.Translations.RemoveRange(db.Translations.Where(t => t.ChildId == id));
        db.Messages.RemoveRange(db.Messages.Where(m => m.SenderId == id));
        db.OtpCodes.RemoveRange(db.OtpCodes.Where(o => o.UserId == id));
        db.BlockedSites.RemoveRange(db.BlockedSites.Where(b => b.ChildId == id));
        await db.SaveChangesAsync();
        await users.DeleteAsync(kid);
        return NoContent();
    }

    private IQueryable<AppUser> Kids() =>
        (from u in db.Users
         join ur in db.UserRoles on u.Id equals ur.UserId
         join r in db.Roles on ur.RoleId equals r.Id
         where u.FamilyId == FamilyId && r.Name == Roles.Child
         select u);
}
