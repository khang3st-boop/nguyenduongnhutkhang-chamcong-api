using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;

namespace NguyenDuongNhutKhang_app.Controllers;

public record TranslationDto(
    [Required, RegularExpression("^(text|photo|realtime)$")] string Kind,
    [Required, StringLength(10)] string SourceLang,
    [Required, StringLength(10)] string TargetLang,
    [Required, StringLength(4000)] string SourceText,
    [Required, StringLength(4000)] string TranslatedText);

/// <summary>Nhật ký dịch: trẻ dịch (ML Kit trên máy) rồi lưu qua API; phụ huynh xem trẻ đã tra gì.</summary>
[Route("api/[controller]")]
public class TranslationsController(AppDbContext db) : ApiBase
{
    [Authorize(Roles = Roles.Child), HttpPost]
    public async Task<IActionResult> Create(TranslationDto dto)
    {
        var t = new TranslationRecord
        {
            FamilyId = FamilyId, ChildId = UserId, Kind = dto.Kind, SourceLang = dto.SourceLang,
            TargetLang = dto.TargetLang, SourceText = dto.SourceText, TranslatedText = dto.TranslatedText
        };
        db.Translations.Add(t);
        await db.SaveChangesAsync();
        return Ok(new { t.Id });
    }

    [Authorize(Roles = $"{Roles.Parent},{Roles.Child}"), HttpGet]
    public async Task<IActionResult> List([FromQuery] string? childId, [FromQuery] string? q)
    {
        var query = db.Translations.Where(t => t.FamilyId == FamilyId);
        if (Role == Roles.Child) query = query.Where(t => t.ChildId == UserId);
        else if (!string.IsNullOrEmpty(childId)) query = query.Where(t => t.ChildId == childId);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(t => t.SourceText.Contains(q) || t.TranslatedText.Contains(q));
        var rows = await (from t in query
                          join u in db.Users on t.ChildId equals u.Id
                          orderby t.Id descending
                          select new
                          {
                              t.Id, t.Kind, t.SourceLang, t.TargetLang, t.SourceText, t.TranslatedText, t.CreatedAt,
                              childId = t.ChildId, childName = u.FullName
                          }).Take(200).ToListAsync();
        return Ok(rows);
    }

    [Authorize(Roles = $"{Roles.Parent},{Roles.Child}"), HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var t = await db.Translations.FirstOrDefaultAsync(x => x.Id == id && x.FamilyId == FamilyId);
        if (t == null || (Role == Roles.Child && t.ChildId != UserId)) return NotFound();
        db.Translations.Remove(t);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
