using System.ComponentModel.DataAnnotations;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NguyenDuongNhutKhang_app.Data;
using NguyenDuongNhutKhang_app.Services;

namespace NguyenDuongNhutKhang_app.Controllers;

public record RegisterDto(
    [Required, StringLength(40, MinimumLength = 3), RegularExpression(@"^[A-Za-z0-9_.]+$")] string Username,
    [EmailAddress] string? Email,
    [Required, MinLength(6)] string Password,
    [Required, StringLength(120)] string FullName,
    [RegularExpression("^(manager|employee)$")] string? Role = null,
    [StringLength(6, MinimumLength = 6)] string? InviteCode = null);

public record LoginDto([Required] string Username, [Required] string Password);
public record OtpDto([Required] string Username, [Required, StringLength(6, MinimumLength = 6)] string Code);
public record UsernameDto([Required] string Username);
public record GoogleDto([Required] string IdToken);

[Route("api/[controller]")]
public class AuthController(
    UserManager<AppUser> users, AppDbContext db, OtpService otp, EmailSender email, JwtService jwt,
    IOptions<GoogleOptions> google, ILogger<AuthController> log) : ApiBase
{
    // POST api/Auth/register : đăng ký quản lý qua email, gửi OTP xác minh email
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        if (await users.FindByNameAsync(dto.Username) != null) return Fail(400, "Tên đăng nhập đã tồn tại.");
        if (dto.Role == "employee") return await RegisterEmployee(dto);
        if (string.IsNullOrWhiteSpace(dto.Email)) return Fail(400, "Vui lòng nhập email.");
        if (await users.FindByEmailAsync(dto.Email) != null) return Fail(400, "Email đã được đăng ký.");

        var user = new AppUser { UserName = dto.Username, Email = dto.Email, FullName = dto.FullName.Trim() };
        var created = await users.CreateAsync(user, dto.Password);
        if (!created.Succeeded)
            return BadRequest(new { status = false, message = string.Join(" ", created.Errors.Select(e => e.Description)) });

        var company = new Company { Name = $"Công ty của {user.FullName}", OwnerId = user.Id };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        user.CompanyId = company.Id;
        await users.UpdateAsync(user);
        await users.AddToRoleAsync(user, Roles.Manager);

        var sent = await SendOtp(user, "verify-email", user.Email!, "xác minh email");
        if (sent is not null) return sent;
        return Ok(new { status = true, message = "Đã gửi mã xác minh tới email.", requiresEmailVerification = true });
    }

    // Nhân viên tự đăng ký bằng mã mời 6 số của quản lý; OTP đăng nhập gửi về email nhân viên (nếu có) hoặc email quản lý.
    private async Task<IActionResult> RegisterEmployee(RegisterDto dto)
    {
        var invite = string.IsNullOrEmpty(dto.InviteCode) ? null
            : await db.CompanyInvites.FirstOrDefaultAsync(i => i.Code == dto.InviteCode && i.UsedAt == null && i.ExpiresAt > DateTime.UtcNow);
        if (invite == null) return Fail(400, "Mã mời không đúng hoặc đã hết hạn. Hãy nhờ quản lý tạo mã mới.");

        var emp = new AppUser { UserName = dto.Username, FullName = dto.FullName.Trim(), EmailConfirmed = true, CompanyId = invite.CompanyId };
        var created = await users.CreateAsync(emp, dto.Password);
        if (!created.Succeeded)
            return BadRequest(new { status = false, message = string.Join(" ", created.Errors.Select(e => e.Description)) });
        await users.AddToRoleAsync(emp, Roles.Employee);
        invite.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(await Session(emp));
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(OtpDto dto)
    {
        var user = await FindUser(dto.Username);
        if (user == null || !await otp.VerifyAsync(user.Id, "verify-email", dto.Code))
            return Fail(400, "Mã không đúng hoặc đã hết hạn.");
        user.EmailConfirmed = true;
        await users.UpdateAsync(user);
        return Ok(await Session(user));
    }

    [HttpPost("resend-email-code")]
    public async Task<IActionResult> ResendEmailCode(UsernameDto dto)
    {
        var user = await FindUser(dto.Username);
        if (user == null || user.EmailConfirmed || user.Email == null) return Ok(new { status = true });
        return await SendOtp(user, "verify-email", user.Email, "xác minh email")
               ?? Ok(new { status = true, message = "Đã gửi lại mã." });
    }

    // POST api/Auth/login : bước 1 - tên đăng nhập + mật khẩu, gửi Email OTP
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var user = await users.FindByNameAsync(dto.Username) ?? await users.FindByEmailAsync(dto.Username);
        if (user == null) return Fail(401, "Sai tên đăng nhập hoặc mật khẩu.");
        if (await users.IsLockedOutAsync(user)) return Fail(403, "Tài khoản đã bị khóa.");
        if (!await users.CheckPasswordAsync(user, dto.Password))
        {
            await users.AccessFailedAsync(user);
            return Fail(401, "Sai tên đăng nhập hoặc mật khẩu.");
        }
        await users.ResetAccessFailedCountAsync(user);

        var role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? "";
        if (role != Roles.Employee && !user.EmailConfirmed)
        {
            await SendOtp(user, "verify-email", user.Email!, "xác minh email");
            return StatusCode(403, new { status = false, message = "Email chưa được xác minh. Đã gửi mã mới.", needsEmailVerification = true, username = user.UserName });
        }

        var target = await OtpTarget(user, role);
        if (target == null) return Fail(400, "Tài khoản chưa có email nhận OTP.");
        var err = await SendOtp(user, "login", target, "đăng nhập");
        if (err is not null) return err;
        return Ok(new { status = true, requiresOtp = true, username = user.UserName, maskedEmail = Mask(target) });
    }

    [HttpPost("resend-login-otp")]
    public async Task<IActionResult> ResendLoginOtp(UsernameDto dto)
    {
        var user = await FindUser(dto.Username);
        if (user == null) return Ok(new { status = true });
        var role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? "";
        var target = await OtpTarget(user, role);
        if (target == null) return Ok(new { status = true });
        return await SendOtp(user, "login", target, "đăng nhập") ?? Ok(new { status = true, message = "Đã gửi lại mã." });
    }

    // POST api/Auth/verify-otp : bước 2 - nhập OTP nhận JWT
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp(OtpDto dto)
    {
        var user = await FindUser(dto.Username);
        if (user == null || await users.IsLockedOutAsync(user) || !await otp.VerifyAsync(user.Id, "login", dto.Code))
            return Fail(401, "Mã OTP không đúng hoặc đã hết hạn.");
        return Ok(await Session(user));
    }

    // POST api/Auth/google : đăng nhập/đăng ký bằng Google OAuth2 (backend xác thực idToken)
    [HttpPost("google")]
    public async Task<IActionResult> Google(GoogleDto dto)
    {
        var ids = google.Value.ClientIds.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (ids.Length == 0) return Fail(503, "Máy chủ chưa cấu hình Google OAuth Client ID.");
        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = ids });
        }
        catch (InvalidJwtException)
        {
            return Fail(401, "Google idToken không hợp lệ.");
        }
        if (!payload.EmailVerified) return Fail(401, "Email Google chưa được xác minh.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == payload.Subject)
                   ?? await users.FindByEmailAsync(payload.Email);
        if (user == null)
        {
            var baseName = new string(payload.Email.Split('@')[0].Where(char.IsLetterOrDigit).ToArray());
            var name = baseName;
            for (var i = 1; await users.FindByNameAsync(name) != null; i++) name = baseName + i;
            user = new AppUser
            {
                UserName = name, Email = payload.Email, EmailConfirmed = true, GoogleSubject = payload.Subject,
                FullName = payload.Name ?? name, AvatarUrl = payload.Picture
            };
            var created = await users.CreateAsync(user);
            if (!created.Succeeded) return Fail(400, string.Join(" ", created.Errors.Select(e => e.Description)));
            var company = new Company { Name = $"Công ty của {user.FullName}", OwnerId = user.Id };
            db.Companies.Add(company);
            await db.SaveChangesAsync();
            user.CompanyId = company.Id;
            await users.UpdateAsync(user);
            await users.AddToRoleAsync(user, Roles.Manager);
        }
        else
        {
            if (await users.IsLockedOutAsync(user)) return Fail(403, "Tài khoản đã bị khóa.");
            if (user.GoogleSubject == null) { user.GoogleSubject = payload.Subject; user.EmailConfirmed = true; await users.UpdateAsync(user); }
        }
        return Ok(await Session(user));
    }

    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await users.FindByIdAsync(UserId);
        if (user == null) return Unauthorized();
        return Ok(await Profile(user));
    }

    // ---- helpers ----
    private async Task<AppUser?> FindUser(string nameOrEmail) =>
        await users.FindByNameAsync(nameOrEmail) ?? await users.FindByEmailAsync(nameOrEmail);

    private async Task<object> Session(AppUser user)
    {
        var role = (await users.GetRolesAsync(user)).First();
        var (token, expires) = jwt.Create(user, role);
        return new { status = true, token, expires, user = await Profile(user) };
    }

    private async Task<object> Profile(AppUser user) => new
    {
        id = user.Id, userName = user.UserName, fullName = user.FullName, email = user.Email,
        avatarUrl = user.AvatarUrl, companyId = user.CompanyId,
        role = (await users.GetRolesAsync(user)).FirstOrDefault()
    };

    // Nhân viên không bắt buộc có email: OTP gửi về email của quản lý chủ công ty.
    private async Task<string?> OtpTarget(AppUser user, string role)
    {
        if (!string.IsNullOrWhiteSpace(user.Email)) return user.Email;
        if (role != Roles.Employee || user.CompanyId == null) return null;
        var ownerId = await db.Companies.Where(f => f.Id == user.CompanyId).Select(f => f.OwnerId).FirstOrDefaultAsync();
        return ownerId == null ? null : (await users.FindByIdAsync(ownerId))?.Email;
    }

    private async Task<IActionResult?> SendOtp(AppUser user, string purpose, string to, string label)
    {
        var code = await otp.IssueAsync(user.Id, purpose);
        if (code == null) return Fail(429, "Vui lòng đợi 30 giây trước khi yêu cầu mã mới.");
        try
        {
            await email.SendOtpAsync(to, code, label);
            return null;
        }
        catch (EmailNotConfiguredException)
        {
            return Fail(503, "Máy chủ chưa cấu hình SMTP gửi email.");
        }
        catch (Exception ex)
        {
            log.LogError("Gửi email OTP thất bại: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return Fail(502, "Không gửi được email OTP. Kiểm tra cấu hình SMTP.");
        }
    }

    private static string Mask(string email)
    {
        var at = email.IndexOf('@');
        return at <= 2 ? email : email[..2] + new string('*', at - 2) + email[at..];
    }
}
