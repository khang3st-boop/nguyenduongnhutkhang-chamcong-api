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
    [StringLength(6, MinimumLength = 6)] string? InviteCode = null,
    [StringLength(20)] string? Phone = null,
    string? Channel = null);

public record LoginDto([Required] string Username, [Required] string Password, string? Channel = null);
public record OtpDto([Required] string Username, [Required, StringLength(6, MinimumLength = 6)] string Code);
public record UsernameDto([Required] string Username, string? Channel = null);
public record GoogleDto([Required] string IdToken, string? Role = null, string? InviteCode = null);
public record UpdateProfileDto([StringLength(100)] string? FullName = null, [EmailAddress] string? Email = null, [StringLength(20)] string? Phone = null, string? Code = null);

[Route("api/[controller]")]
public class AuthController(
    UserManager<AppUser> users, AppDbContext db, OtpService otp, EmailSender email, SmsSender sms, JwtService jwt,
    IOptions<GoogleOptions> google, ILogger<AuthController> log) : ApiBase
{
    // POST api/Auth/register : đăng ký quản lý qua email, gửi OTP xác minh email
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        if (await users.FindByNameAsync(dto.Username) != null) return Fail(400, "Tên đăng nhập đã tồn tại.");
        var phone = SmsSender.Normalize(dto.Phone);
        if (!string.IsNullOrWhiteSpace(dto.Phone) && phone == null) return Fail(400, "Số điện thoại không hợp lệ.");
        if (dto.Role == "employee") return await RegisterEmployee(dto, phone);
        if (string.IsNullOrWhiteSpace(dto.Email)) return Fail(400, "Vui lòng nhập email.");
        var viaSms = string.Equals(dto.Channel, "sms", StringComparison.OrdinalIgnoreCase);
        if (viaSms && phone == null) return Fail(400, "Cần nhập số điện thoại để nhận SMS OTP.");
        if (await users.FindByEmailAsync(dto.Email) != null) return Fail(400, "Email đã được đăng ký.");

        var user = new AppUser { UserName = dto.Username, Email = dto.Email, FullName = dto.FullName.Trim(), PhoneNumber = phone };
        var created = await users.CreateAsync(user, dto.Password);
        if (!created.Succeeded)
            return BadRequest(new { status = false, message = string.Join(" ", created.Errors.Select(e => e.Description)) });

        var company = new Company { Name = $"Công ty của {user.FullName}", OwnerId = user.Id };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        user.CompanyId = company.Id;
        await users.UpdateAsync(user);
        await users.AddToRoleAsync(user, Roles.Manager);
        await users.AddClaimAsync(user, new System.Security.Claims.Claim(Approval.Type, Approval.Pending)); // chờ admin/quản lý khác duyệt

        // Người đăng ký chọn nhận mã xác minh qua email hay SMS; gửi lỗi thì xóa tài khoản vừa tạo để đăng ký lại được.
        var target = viaSms ? phone! : user.Email!;
        var sent = await SendOtp(user, "verify-email", target, "xác minh tài khoản", viaSms);
        if (sent is not null)
        {
            await users.DeleteAsync(user);
            db.Companies.Remove(company);
            await db.SaveChangesAsync();
            return sent;
        }
        return Ok(new
        {
            status = true, message = viaSms ? "Đã gửi mã xác minh qua SMS." : "Đã gửi mã xác minh tới email.", requiresEmailVerification = true,
            channel = viaSms ? "sms" : "email", maskedTarget = viaSms ? SmsSender.Mask(target) : Mask(target)
        });
    }

    // Nhân viên tự đăng ký bằng mã mời 6 số của quản lý; OTP đăng nhập gửi về email nhân viên (nếu có) hoặc email quản lý.
    private async Task<IActionResult> RegisterEmployee(RegisterDto dto, string? phone)
    {
        var invite = string.IsNullOrEmpty(dto.InviteCode) ? null
            : await db.CompanyInvites.FirstOrDefaultAsync(i => i.Code == dto.InviteCode && i.UsedAt == null && i.ExpiresAt > DateTime.UtcNow);
        if (invite == null) return Fail(400, "Mã mời không đúng hoặc đã hết hạn. Hãy nhờ quản lý tạo mã mới.");

        if (string.IsNullOrWhiteSpace(dto.Email)) return Fail(400, "Vui lòng nhập Gmail/email để nhận mã xác minh.");
        if (await users.FindByEmailAsync(dto.Email) != null) return Fail(400, "Email đã được đăng ký.");

        var emp = new AppUser { UserName = dto.Username, Email = dto.Email, FullName = dto.FullName.Trim(), CompanyId = invite.CompanyId, PhoneNumber = phone };
        var created = await users.CreateAsync(emp, dto.Password);
        if (!created.Succeeded)
            return BadRequest(new { status = false, message = string.Join(" ", created.Errors.Select(e => e.Description)) });
        await users.AddToRoleAsync(emp, Roles.Employee);
        invite.UsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        // Nhân viên cũng phải xác minh email bằng OTP; gửi lỗi thì hoàn tác để đăng ký lại được.
        var sent = await SendOtp(emp, "verify-email", emp.Email!, "xác minh tài khoản");
        if (sent is not null)
        {
            await users.DeleteAsync(emp);
            invite.UsedAt = null;
            await db.SaveChangesAsync();
            return sent;
        }
        return Ok(new { status = true, message = "Đã gửi mã xác minh tới email.", requiresEmailVerification = true, channel = "email", maskedTarget = Mask(emp.Email!) });
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(OtpDto dto)
    {
        var user = await FindUser(dto.Username);
        if (user == null || !await otp.VerifyAsync(user.Id, "verify-email", dto.Code))
            return Fail(400, "Mã không đúng hoặc đã hết hạn.");
        user.EmailConfirmed = true;
        await users.UpdateAsync(user);
        if (await IsPending(user))
            return Ok(new { status = true, pendingApproval = true, message = PendingMsg });
        return Ok(await Session(user));
    }

    [HttpPost("resend-email-code")]
    public async Task<IActionResult> ResendEmailCode(UsernameDto dto)
    {
        var user = await FindUser(dto.Username);
        if (user == null || user.EmailConfirmed || user.Email == null) return Ok(new { status = true });
        var viaSms = string.Equals(dto.Channel, "sms", StringComparison.OrdinalIgnoreCase);
        var phone = SmsSender.Normalize(user.PhoneNumber);
        if (viaSms && phone == null) return Ok(new { status = true });
        return await SendOtp(user, "verify-email", viaSms ? phone! : user.Email, "xác minh tài khoản", viaSms)
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
        if (!user.EmailConfirmed && user.Email != null)
        {
            await SendOtp(user, "verify-email", user.Email!, "xác minh email");
            return StatusCode(403, new { status = false, message = "Email chưa được xác minh. Đã gửi mã mới.", needsEmailVerification = true, username = user.UserName });
        }
        if (await IsPending(user)) return StatusCode(403, new { status = false, message = PendingMsg, pendingApproval = true });

        var (target, isSms) = await ResolveTarget(user, role, dto.Channel);
        if (target == null)
            return Fail(400, isSms ? "Tài khoản chưa có số điện thoại nhận OTP." : "Tài khoản chưa có email nhận OTP.");
        var err = await SendOtp(user, "login", target, "đăng nhập", isSms);
        if (err is not null) return err;
        var masked = isSms ? SmsSender.Mask(target) : Mask(target);
        return Ok(new { status = true, requiresOtp = true, username = user.UserName, channel = isSms ? "sms" : "email", maskedTarget = masked, maskedEmail = masked });
    }

    [HttpPost("resend-login-otp")]
    public async Task<IActionResult> ResendLoginOtp(UsernameDto dto)
    {
        var user = await FindUser(dto.Username);
        if (user == null) return Ok(new { status = true });
        var role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? "";
        var (target, isSms) = await ResolveTarget(user, role, dto.Channel);
        if (target == null) return Ok(new { status = true });
        return await SendOtp(user, "login", target, "đăng nhập", isSms) ?? Ok(new { status = true, message = "Đã gửi lại mã." });
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
            // Gmail chưa có trong hệ thống: chỉ được tạo tài khoản NHÂN VIÊN bằng mã mời của quản lý (quản lý đăng ký bằng form email + OTP).
            if (string.IsNullOrEmpty(dto.InviteCode))
                return Ok(new { status = true, needsRole = true, email = payload.Email, name = payload.Name });
            var invite = await db.CompanyInvites.FirstOrDefaultAsync(i => i.Code == dto.InviteCode && i.UsedAt == null && i.ExpiresAt > DateTime.UtcNow);
            if (invite == null) return Fail(400, "Mã mời không đúng hoặc đã hết hạn. Hãy nhờ quản lý tạo mã mới.");
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
            user.CompanyId = invite.CompanyId;
            await users.UpdateAsync(user);
            await users.AddToRoleAsync(user, Roles.Employee);
            invite.UsedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Ok(await Session(user));
        }
        else
        {
            if (await users.IsLockedOutAsync(user)) return Fail(403, "Tài khoản đã bị khóa.");
            if (await IsPending(user)) return StatusCode(403, new { status = false, message = PendingMsg, pendingApproval = true });
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

    // Đổi email / số điện thoại cần OTP (bảo mật): bước 1 yêu cầu mã, bước 2 gửi kèm mã. Đổi họ tên thì không cần.
    // OTP gửi SMS tới số MỚI (chứng minh số thuộc về bạn); nếu chỉ đổi email thì gửi SMS tới số hiện tại
    // (nhân viên chưa có số thì dùng số quản lý); không có số nào mới gửi email hiện tại.
    [Authorize, HttpPost("me/request-change")]
    public async Task<IActionResult> RequestProfileChange(UpdateProfileDto dto)
    {
        var user = await users.FindByIdAsync(UserId);
        if (user == null) return Unauthorized();
        var role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? "";
        var (phone, newEmail, err) = await ParseChange(user, role, dto);
        if (err != null) return err;
        if (!ContactChanged(user, phone, newEmail)) return Ok(new { status = true, needsOtp = false });

        // Đổi email: mã OTP gửi về email hiện tại của tài khoản (không dùng SMS).
        var target = await OtpTarget(user, role);
        const bool isSms = false;
        if (target == null) return Fail(400, "Không có số điện thoại hay email nào để gửi mã xác nhận.");
        var sent = await SendOtp(user, ChangePurpose(phone, newEmail), target, "đổi thông tin tài khoản", isSms);
        if (sent is not null) return sent;
        return Ok(new { status = true, needsOtp = true, channel = isSms ? "sms" : "email", maskedTarget = isSms ? SmsSender.Mask(target) : Mask(target) });
    }

    // PUT api/Auth/me : tự chỉnh họ tên, email, số điện thoại nhận OTP (mặc định nhân viên dùng email/SĐT của quản lý)
    [Authorize, HttpPut("me")]
    public async Task<IActionResult> UpdateMe(UpdateProfileDto dto)
    {
        var user = await users.FindByIdAsync(UserId);
        if (user == null) return Unauthorized();
        var role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? "";
        var (phone, newEmail, err) = await ParseChange(user, role, dto);
        if (err != null) return err;

        if (ContactChanged(user, phone, newEmail))
        {
            if (string.IsNullOrWhiteSpace(dto.Code) || !await otp.VerifyAsync(user.Id, ChangePurpose(phone, newEmail), dto.Code))
                return Fail(403, "Đổi email/số điện thoại cần mã OTP xác nhận đúng.");
            user.PhoneNumber = phone;
            if (!string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
            {
                user.Email = newEmail;
                // Đã xác nhận bằng OTP gửi tới số điện thoại nên email mới được coi là đã xác minh.
                if (newEmail != null) user.EmailConfirmed = true;
            }
        }
        if (!string.IsNullOrWhiteSpace(dto.FullName)) user.FullName = dto.FullName.Trim();
        var res = await users.UpdateAsync(user);
        if (!res.Succeeded) return Fail(400, string.Join(" ", res.Errors.Select(e => e.Description)));
        return Ok(await Profile(user));
    }

    private async Task<(string? phone, string? email, IActionResult? error)> ParseChange(AppUser user, string role, UpdateProfileDto dto)
    {
        var phone = SmsSender.Normalize(dto.Phone);
        if (!string.IsNullOrWhiteSpace(dto.Phone) && phone == null) return (null, null, Fail(400, "Số điện thoại không hợp lệ."));
        var email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
        if (email == null && role != Roles.Employee) return (null, null, Fail(400, "Quản lý phải có email."));
        if (email != null && !string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var other = await users.FindByEmailAsync(email);
            if (other != null && other.Id != user.Id) return (null, null, Fail(400, "Email đã được dùng bởi tài khoản khác."));
        }
        return (phone, email, null);
    }

    private static bool ContactChanged(AppUser user, string? phone, string? email) =>
        phone != SmsSender.Normalize(user.PhoneNumber) || !string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase);

    // Mã chỉ dùng được cho đúng cặp (email, số) đã yêu cầu.
    private static string ChangePurpose(string? phone, string? email) =>
        "profile-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{phone}|{email?.ToLowerInvariant()}")))[..12];

    // ---- helpers ----
    private async Task<AppUser?> FindUser(string nameOrEmail) =>
        await users.FindByNameAsync(nameOrEmail) ?? await users.FindByEmailAsync(nameOrEmail);

    private const string PendingMsg = "Tài khoản quản lý đang chờ admin hoặc một quản lý khác duyệt. Bạn sẽ đăng nhập được sau khi được duyệt.";
    private async Task<bool> IsPending(AppUser user) =>
        (await users.GetClaimsAsync(user)).Any(c => c.Type == Approval.Type && c.Value == Approval.Pending);

    private async Task<object> Session(AppUser user)
    {
        var role = (await users.GetRolesAsync(user)).First();
        var (token, expires) = jwt.Create(user, role);
        return new { status = true, token, expires, user = await Profile(user) };
    }

    private async Task<object> Profile(AppUser user) => new
    {
        id = user.Id, userName = user.UserName, fullName = user.FullName, email = user.Email,
        phone = user.PhoneNumber, avatarUrl = user.AvatarUrl, companyId = user.CompanyId,
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

    // Kênh OTP: "sms" gửi tới số điện thoại của người dùng (nhân viên chưa có số thì dùng số của quản lý); còn lại gửi email.
    private async Task<(string? target, bool isSms)> ResolveTarget(AppUser user, string role, string? channel)
    {
        if (!string.Equals(channel, "sms", StringComparison.OrdinalIgnoreCase)) return (await OtpTarget(user, role), false);
        var phone = SmsSender.Normalize(user.PhoneNumber);
        if (phone == null && role == Roles.Employee && user.CompanyId != null)
        {
            var ownerId = await db.Companies.Where(f => f.Id == user.CompanyId).Select(f => f.OwnerId).FirstOrDefaultAsync();
            if (ownerId != null) phone = SmsSender.Normalize((await users.FindByIdAsync(ownerId))?.PhoneNumber);
        }
        return (phone, true);
    }

    private async Task<IActionResult?> SendOtp(AppUser user, string purpose, string to, string label, bool viaSms = false)
    {
        var code = await otp.IssueAsync(user.Id, purpose);
        if (code == null) return Fail(429, "Vui lòng đợi 30 giây trước khi yêu cầu mã mới.");
        try
        {
            if (viaSms) await sms.SendOtpAsync(to, code);
            else await email.SendOtpAsync(to, code, label);
            return null;
        }
        catch (SmsNotConfiguredException)
        {
            return Fail(503, "Máy chủ chưa cấu hình SMS (Twilio).");
        }
        catch (EmailNotConfiguredException)
        {
            return Fail(503, "Máy chủ chưa cấu hình SMTP gửi email.");
        }
        catch (Exception ex)
        {
            log.LogError("Gửi OTP ({Kind}) thất bại: {Type}: {Message}", viaSms ? "sms" : "email", ex.GetType().Name, ex.Message);
            return Fail(502, viaSms ? "Không gửi được SMS OTP. Kiểm tra cấu hình Twilio và số điện thoại đã xác minh." : "Không gửi được email OTP. Kiểm tra cấu hình SMTP.");
        }
    }

    private static string Mask(string email)
    {
        var at = email.IndexOf('@');
        return at <= 2 ? email : email[..2] + new string('*', at - 2) + email[at..];
    }
}
