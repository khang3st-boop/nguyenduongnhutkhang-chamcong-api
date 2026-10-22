using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;

namespace NguyenDuongNhutKhang_app.Controllers;

public record TextDto([Required, StringLength(2000, MinimumLength = 1)] string Text);

/// <summary>Nhắn tin gia đình (CRUD). Cuộc gọi video nhóm được mời qua một tin nhắn loại "call".</summary>
[Authorize(Roles = $"{Roles.Parent},{Roles.Child}"), Route("api/[controller]")]
public class ChatController(AppDbContext db) : ApiBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int afterId = 0)
    {
        var rows = await (from m in db.Messages
                          join u in db.Users on m.SenderId equals u.Id
                          where m.FamilyId == FamilyId && m.Id > afterId
                          orderby m.Id descending
                          select new
                          {
                              m.Id, m.Kind, m.Text, m.Channel, m.CreatedAt, m.EditedAt,
                              senderId = m.SenderId, senderName = u.FullName
                          }).Take(100).ToListAsync();
        rows.Reverse();
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Send(TextDto dto)
    {
        var m = new ChatMessage { FamilyId = FamilyId, SenderId = UserId, Text = dto.Text.Trim() };
        db.Messages.Add(m);
        await db.SaveChangesAsync();
        return Ok(new { m.Id });
    }

    // Bắt đầu cuộc gọi video nhóm: tạo kênh riêng và đăng thông báo vào khung chat để mọi người bấm tham gia.
    [HttpPost("call")]
    public async Task<IActionResult> StartCall()
    {
        var me = await db.Users.Where(u => u.Id == UserId).Select(u => u.FullName).FirstAsync();
        var channel = $"family-{FamilyId}-{Guid.NewGuid():N}"[..24];
        var m = new ChatMessage
        {
            FamilyId = FamilyId, SenderId = UserId, Kind = "call", Channel = channel,
            Text = $"{me} đã bắt đầu cuộc gọi video nhóm."
        };
        db.Messages.Add(m);
        await db.SaveChangesAsync();
        return Ok(new { m.Id, channel });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Edit(int id, TextDto dto)
    {
        var m = await db.Messages.FirstOrDefaultAsync(x => x.Id == id && x.FamilyId == FamilyId && x.SenderId == UserId && x.Kind == "text");
        if (m == null) return NotFound();
        m.Text = dto.Text.Trim();
        m.EditedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var m = await db.Messages.FirstOrDefaultAsync(x => x.Id == id && x.FamilyId == FamilyId);
        if (m == null || (m.SenderId != UserId && Role != Roles.Parent)) return NotFound();
        db.Messages.Remove(m);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
