using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using NguyenDuongNhutKhang_app.Data;

namespace NguyenDuongNhutKhang_app.Services;

public class JwtService(IConfiguration config)
{
    public (string token, DateTime expires) Create(AppUser user, string role)
    {
        var s = config.GetSection("JWTKey");
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? ""),
            new(ClaimTypes.Role, role),
            new("company_id", user.CompanyId?.ToString() ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var expires = DateTime.UtcNow.AddHours(double.Parse(s["TokenExpiryTimeInHour"] ?? "8"));
        var jwt = new JwtSecurityToken(s["ValidIssuer"], s["ValidAudience"], claims, expires: expires,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(s["Secret"]!)), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}
