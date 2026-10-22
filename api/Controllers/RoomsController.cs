using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;

namespace NguyenDuongNhutKhang_app.Controllers;

public record RoomDto([Required, StringLength(60, MinimumLength = 1)] string Name);
public record RoomChannelDto([Required] string Channel);

/// <summary>Ai đang ở trong phòng nào (bộ nhớ máy chủ, hết hạn sau 40 giây nếu app ngừng gửi ping).</summary>
public class RoomPresence
{
    private readonly ConcurrentDictionary<(string Channel, string UserId), (string Name, DateTime Seen)> _in = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(40);

    public void Ping(string channel, string userId, string name) => _in[(channel, userId)] = (name, DateTime.UtcNow);
    public void Leave(string channel, string userId) => _in.TryRemove((channel, userId), out _);

    public List<string> Who(string channel)
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _in.Where(kv => now - kv.Value.Seen > Ttl)) _in.TryRemove(kv.Key, out _);
        return _in.Where(kv => kv.Key.Channel == channel).Select(kv => kv.Value.Name).Order().ToList();
    }
}

/// <summary>
/// Phòng họp video riêng trong công ty: "Phòng chung" luôn có, ngoài ra mọi người tạo thêm phòng theo nhóm/dự án.
/// Mỗi phòng có kênh Agora riêng nên cuộc gọi của phòng này không lẫn sang phòng khác.
/// Phòng lưu trong bảng Messages với Kind = "room" (Text = tên phòng) để không phải đổi cấu trúc CSDL.
/// </summary>
[Authorize(Roles = $"{Roles.Manager},{Roles.Employee}"), Route("api/[controller]")]
public class RoomsController(AppDbContext db, RoomPresence presence) : ApiBase
{
    private string General => $"team-{CompanyId}-general";

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var rooms = await (from m in db.Messages
                           join u in db.Users on m.SenderId equals u.Id
                           where m.CompanyId == CompanyId && m.Kind == "room"
                           orderby m.Id
                           select new { m.Id, m.Text, m.Channel, m.CreatedAt, createdBy = m.SenderId, createdByName = u.FullName }).ToListAsync();
        var list = new List<object> { new { id = 0, name = "Phòng chung", channel = General, isGeneral = true, canEdit = false, createdByName = "", participants = presence.Who(General) } };
        list.AddRange(rooms.Select(r => new
        {
            r.Id, name = r.Text, channel = r.Channel, isGeneral = false, canEdit = r.createdBy == UserId || Role == Roles.Manager,
            r.createdByName, participants = presence.Who(r.Channel!)
        }));
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create(RoomDto dto)
    {
        if (await db.Messages.CountAsync(m => m.CompanyId == CompanyId && m.Kind == "room") >= 30)
            return Fail(400, "Công ty đã có tối đa 30 phòng. Hãy xóa bớt phòng không dùng.");
        var channel = $"team-{CompanyId}-r{Guid.NewGuid():N}"[..28];
        var m = new ChatMessage { CompanyId = CompanyId, SenderId = UserId, Kind = "room", Text = dto.Name.Trim(), Channel = channel };
        db.Messages.Add(m);
        await db.SaveChangesAsync();
        return Ok(new { m.Id, name = m.Text, channel });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Rename(int id, RoomDto dto)
    {
        var m = await Find(id);
        if (m == null) return NotFound();
        m.Text = dto.Name.Trim();
        m.EditedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var m = await Find(id);
        if (m == null) return NotFound();
        db.Messages.Remove(m);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // App gửi ping mỗi 15 giây khi đang ở trong phòng để hiện "đang họp: ...".
    [HttpPost("ping")]
    public async Task<IActionResult> Ping(RoomChannelDto dto)
    {
        if (!dto.Channel.StartsWith($"team-{CompanyId}-") || dto.Channel.Length > 64) return Fail(403, "Không có quyền vào phòng này.");
        var name = await db.Users.Where(u => u.Id == UserId).Select(u => u.FullName).FirstAsync();
        presence.Ping(dto.Channel, UserId, name);
        return NoContent();
    }

    [HttpPost("leave")]
    public IActionResult Leave(RoomChannelDto dto)
    {
        presence.Leave(dto.Channel, UserId);
        return NoContent();
    }

    private Task<ChatMessage?> Find(int id) =>
        db.Messages.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == CompanyId && x.Kind == "room" && (x.SenderId == UserId || Role == Roles.Manager));
}
