using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NguyenDuongNhutKhang_app.Data;
using NguyenDuongNhutKhang_app.Services;

namespace NguyenDuongNhutKhang_app.Controllers;

public record VideoTokenDto([Required] string Channel);

/// <summary>Cấp token Agora để vào phòng gọi video nhóm của đúng công ty.</summary>
[Authorize(Roles = $"{Roles.Manager},{Roles.Employee}"), Route("api/[controller]")]
public class VideoController(IOptions<AgoraOptions> options) : ApiBase
{
    [HttpPost("token")]
    public IActionResult Token(VideoTokenDto dto)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.AppId)) return Fail(503, "Máy chủ chưa cấu hình Agora AppId.");
        if (!dto.Channel.StartsWith($"team-{CompanyId}") || dto.Channel.Length > 64) return Fail(403, "Không có quyền vào phòng này.");

        var uid = BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(UserId)), 0) & 0x7FFFFFFF;
        if (uid == 0) uid = 1;
        var token = string.IsNullOrWhiteSpace(o.AppCertificate)
            ? ""
            : AgoraTokenBuilder.BuildRtcToken(o.AppId, o.AppCertificate, dto.Channel, uid, 3600);
        return Ok(new { appId = o.AppId, channel = dto.Channel, uid, token, expiresInSeconds = 3600 });
    }
}
