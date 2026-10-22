using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NguyenDuongNhutKhang_app.Data;

namespace NguyenDuongNhutKhang_app.Controllers;

public record ChatTurn(string Role, string Text);
public record AssistantDto([Required, MinLength(1)] List<ChatTurn> Messages);

/// <summary>
/// Trợ lý AI (Gemini) cho nhân viên và quản lý. Gemini không tự biết dữ liệu: nó gọi các công cụ (function calling) bên dưới,
/// mỗi công cụ chỉ đọc dữ liệu trong phạm vi vai trò của người đang hỏi (nhân viên chỉ thấy của mình, quản lý thấy cả công ty).
/// Khóa API đặt ở biến môi trường Gemini__ApiKey trên server, không bao giờ nằm trong app.
/// </summary>
[Authorize(Roles = $"{Roles.Employee},{Roles.Manager}"), Route("api/[controller]")]
public class AssistantController(AppDbContext db, IConfiguration cfg, ILogger<AssistantController> log) : ApiBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(40) };
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);
    private static DateTime Local(DateTime utc) => utc + Vn;
    private static string Fmt(DateTime? utc) => utc == null ? "chưa ra" : Local(utc.Value).ToString("yyyy-MM-dd HH:mm");
    private static string Hm(int m) => $"{m / 60:00}:{m % 60:00}";

    private bool IsManager => Role == Roles.Manager;

    // POST api/Assistant/chat : { messages: [{ role: "user"|"model", text }] } -> { reply }
    [HttpPost("chat")]
    public async Task<IActionResult> Chat(AssistantDto dto, CancellationToken ct)
    {
        var key = cfg["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            return Fail(503, "Trợ lý AI chưa được cấu hình (thiếu Gemini__ApiKey trên server).");
        var model = cfg["Gemini:Model"] is { Length: > 0 } m ? m : "gemini-2.5-flash";

        var contents = new JsonArray();
        foreach (var t in dto.Messages.TakeLast(12))
        {
            var text = (t.Text ?? "").Trim();
            if (text.Length == 0) continue;
            if (text.Length > 1000) text = text[..1000];
            contents.Add(new JsonObject
            {
                ["role"] = t.Role == "model" ? "model" : "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = text }),
            });
        }
        if (contents.Count == 0 || contents[^1]!["role"]!.GetValue<string>() != "user")
            return Fail(400, "Tin nhắn cuối phải là của người dùng.");

        var me = await db.Users.Where(u => u.Id == UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "";
        var system =
            "Bạn là trợ lý của ứng dụng chấm công cho nhân viên và quản lý. " +
            $"Người đang hỏi: {me}, vai trò {(IsManager ? "quản lý" : "nhân viên")}. " +
            $"Bây giờ là {Local(DateTime.UtcNow):yyyy-MM-dd HH:mm} (giờ Việt Nam). " +
            "Trả lời ngắn gọn, thân thiện, đúng ngôn ngữ mà người dùng đang dùng. " +
            "Mọi số liệu chấm công PHẢI lấy từ công cụ, tuyệt đối không bịa; nếu công cụ không có dữ liệu thì nói rõ là chưa có. " +
            "Không tiết lộ dữ liệu ngoài quyền của người hỏi. Với việc không liên quan đến chấm công/công việc, từ chối lịch sự.";

        try
        {
            for (var step = 0; step < 5; step++)
            {
                var body = new JsonObject
                {
                    ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system }) },
                    ["contents"] = JsonNode.Parse(contents.ToJsonString()),
                    ["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = Declarations() }),
                };
                using var req = new HttpRequestMessage(HttpMethod.Post,
                    $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent")
                {
                    Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
                };
                req.Headers.Add("x-goog-api-key", key);
                using var res = await Http.SendAsync(req, ct);
                var raw = await res.Content.ReadAsStringAsync(ct);
                if (!res.IsSuccessStatusCode)
                {
                    log.LogWarning("Gemini {Status}: {Body}", (int)res.StatusCode, raw.Length > 400 ? raw[..400] : raw);
                    return (int)res.StatusCode == 429
                        ? Fail(429, "Trợ lý AI đang quá tải (hết lượt miễn phí), thử lại sau ít phút.")
                        : Fail(502, $"Trợ lý AI chưa trả lời được (Gemini {(int)res.StatusCode}: {GeminiError(raw)}).");
                }

                var cand = JsonNode.Parse(raw)?["candidates"]?[0]?["content"];
                var parts = cand?["parts"] as JsonArray;
                if (parts == null) return Fail(502, "Trợ lý AI không có câu trả lời (có thể bị bộ lọc nội dung chặn).");

                var calls = parts.Where(p => p?["functionCall"] != null).ToList();
                if (calls.Count == 0)
                {
                    var reply = string.Concat(parts.Select(p => p?["text"]?.GetValue<string>() ?? "")).Trim();
                    return Ok(new { status = true, reply = reply.Length == 0 ? "Mình chưa có câu trả lời cho việc này." : reply });
                }

                // Giữ nguyên lượt trả lời của model (kể cả chữ ký suy luận), rồi gửi kết quả công cụ.
                contents.Add(JsonNode.Parse(cand!.ToJsonString()));
                var responses = new JsonArray();
                foreach (var c in calls)
                {
                    var name = c!["functionCall"]!["name"]!.GetValue<string>();
                    var args = c["functionCall"]!["args"] as JsonObject ?? new JsonObject();
                    object result;
                    try { result = await RunTool(name, args, ct); }
                    catch (Exception ex) { log.LogWarning(ex, "Công cụ {Tool} lỗi", name); result = new { error = "Không đọc được dữ liệu." }; }
                    responses.Add(new JsonObject
                    {
                        ["functionResponse"] = new JsonObject
                        {
                            ["name"] = name,
                            ["response"] = new JsonObject { ["result"] = JsonSerializer.SerializeToNode(result) },
                        },
                    });
                }
                contents.Add(new JsonObject { ["role"] = "user", ["parts"] = responses });
            }
            return Fail(502, "Trợ lý AI xử lý quá lâu, hãy hỏi gọn hơn.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning("Gọi Gemini thất bại: {Type}: {Message}", ex.GetType().Name, ex.Message);
            return Fail(502, "Không kết nối được trợ lý AI, vui lòng thử lại.");
        }
    }

    // Lý do Google trả lỗi (không chứa khóa), để chẩn đoán cấu hình sai.
    private static string GeminiError(string raw)
    {
        try
        {
            var msg = JsonNode.Parse(raw)?["error"]?["message"]?.GetValue<string>() ?? raw;
            return msg.Length > 160 ? msg[..160] : msg;
        }
        catch { return raw.Length > 160 ? raw[..160] : raw; }
    }

    // ---- Công cụ cho Gemini -------------------------------------------------------------------------------------

    private static JsonObject Fn(string name, string desc, JsonObject? props = null)
    {
        var f = new JsonObject { ["name"] = name, ["description"] = desc };
        if (props != null) f["parameters"] = new JsonObject { ["type"] = "OBJECT", ["properties"] = props };
        return f;
    }

    private static JsonObject Str(string desc) => new() { ["type"] = "STRING", ["description"] = desc };

    private JsonArray Declarations()
    {
        var month = new JsonObject { ["month"] = Str("Tháng cần xem, dạng yyyy-MM. Bỏ trống = tháng hiện tại.") };
        var arr = new JsonArray
        {
            Fn("get_workplaces", "Danh sách địa điểm chấm công của công ty: giờ vào ca, giờ tan ca, bán kính cho phép."),
        };
        if (IsManager)
        {
            arr.Add(Fn("get_live_status", "Tình hình chấm công HÔM NAY của cả công ty: bao nhiêu người có mặt, đang trong ca, đi trễ; ai đang làm."));
            arr.Add(Fn("get_month_report", "Báo cáo cả tháng của từng nhân viên: số ngày công, số lần đi trễ, phút trễ, số lần về sớm, tổng giờ làm, số lần quên chấm ra.", month));
            arr.Add(Fn("get_employee_attendance", "Chi tiết chấm công trong tháng của MỘT nhân viên, tìm theo tên.",
                new JsonObject { ["employee_name"] = Str("Tên (hoặc một phần tên) của nhân viên."), ["month"] = month["month"]!.DeepClone() }));
        }
        else
        {
            arr.Add(Fn("get_today_status", "Trạng thái chấm công hôm nay của chính người hỏi: đã vào ca chưa, giờ vào, giờ ra."));
            arr.Add(Fn("get_my_attendance", "Lịch sử chấm công của chính người hỏi trong một tháng, kèm số ngày công, lần đi trễ, tổng giờ làm.", month));
        }
        return arr;
    }

    private async Task<object> RunTool(string name, JsonObject args, CancellationToken ct)
    {
        string? Arg(string k) => args[k]?.GetValue<string>();
        switch (name)
        {
            case "get_workplaces":
                return await db.Workplaces.Where(z => z.CompanyId == CompanyId).Select(z => new
                {
                    z.Name, startTime = Hm(z.StartMinute), endTime = Hm(z.EndMinute), z.RadiusMeters, graceMinutes = z.GraceMinutes,
                }).ToListAsync(ct);

            case "get_today_status":
            {
                var day = Local(DateTime.UtcNow).Date - Vn;
                var rows = await db.Attendances.Where(a => a.UserId == UserId && a.CheckIn >= day).OrderBy(a => a.CheckIn).ToListAsync(ct);
                return new
                {
                    working = rows.Any(a => a.CheckOut == null),
                    records = rows.Select(a => new { checkIn = Fmt(a.CheckIn), checkOut = Fmt(a.CheckOut), a.Status, a.LateMinutes }),
                    note = rows.Count == 0 ? "Hôm nay chưa chấm công." : null,
                };
            }

            case "get_my_attendance":
            {
                var (from, to) = MonthRange(Arg("month"));
                var rows = await db.Attendances.Where(a => a.UserId == UserId && a.CheckIn >= from && a.CheckIn < to)
                    .OrderBy(a => a.CheckIn).ToListAsync(ct);
                return Summarize(rows, from);
            }

            case "get_live_status" when IsManager:
            {
                var day = Local(DateTime.UtcNow).Date - Vn;
                var rows = await db.Attendances.Where(a => a.CompanyId == CompanyId && a.CheckIn >= day).ToListAsync(ct);
                var names = await Names(ct);
                var total = await (from u in db.Users join ur in db.UserRoles on u.Id equals ur.UserId join r in db.Roles on ur.RoleId equals r.Id
                                   where u.CompanyId == CompanyId && r.Name == Roles.Employee select u.Id).CountAsync(ct);
                return new
                {
                    totalEmployees = total,
                    presentToday = rows.Select(a => a.UserId).Distinct().Count(),
                    lateToday = rows.Where(a => a.Status == "Late").Select(a => names.GetValueOrDefault(a.UserId, "?")).Distinct(),
                    workingNow = rows.Where(a => a.CheckOut == null).Select(a => new { name = names.GetValueOrDefault(a.UserId, "?"), since = Fmt(a.CheckIn) }),
                    notCheckedInYet = await NotCheckedIn(rows.Select(a => a.UserId).ToHashSet(), ct),
                };
            }

            case "get_month_report" when IsManager:
            {
                var (from, to) = MonthRange(Arg("month"));
                var rows = await db.Attendances.Where(a => a.CompanyId == CompanyId && a.CheckIn >= from && a.CheckIn < to).ToListAsync(ct);
                var names = await Names(ct);
                return new
                {
                    month = Local(from).ToString("yyyy-MM"),
                    employees = rows.GroupBy(a => a.UserId).Select(g => new
                    {
                        name = names.GetValueOrDefault(g.Key, "(đã xóa)"),
                        workDays = g.Select(a => Local(a.CheckIn).Date).Distinct().Count(),
                        lateCount = g.Count(a => a.Status == "Late"),
                        lateMinutes = g.Sum(a => a.LateMinutes),
                        earlyLeaveCount = g.Count(a => a.EarlyLeaveMinutes > 0),
                        hours = Math.Round(g.Where(a => a.CheckOut != null).Sum(a => (a.CheckOut!.Value - a.CheckIn).TotalHours), 1),
                        missingCheckout = g.Count(a => a.CheckOut == null),
                    }).OrderByDescending(x => x.lateCount),
                };
            }

            case "get_employee_attendance" when IsManager:
            {
                var q = (Arg("employee_name") ?? "").Trim();
                if (q.Length == 0) return new { error = "Thiếu tên nhân viên." };
                var matches = await db.Users.Where(u => u.CompanyId == CompanyId && u.FullName.ToLower().Contains(q.ToLower()))
                    .Select(u => new { u.Id, u.FullName }).Take(5).ToListAsync(ct);
                if (matches.Count == 0) return new { error = $"Không tìm thấy nhân viên tên \"{q}\"." };
                if (matches.Count > 1) return new { error = "Có nhiều người trùng tên, hãy hỏi lại rõ hơn.", candidates = matches.Select(m => m.FullName) };
                var (from, to) = MonthRange(Arg("month"));
                var rows = await db.Attendances.Where(a => a.UserId == matches[0].Id && a.CompanyId == CompanyId && a.CheckIn >= from && a.CheckIn < to)
                    .OrderBy(a => a.CheckIn).ToListAsync(ct);
                return new { employee = matches[0].FullName, summary = Summarize(rows, from) };
            }

            default:
                return new { error = "Công cụ không tồn tại hoặc bạn không có quyền dùng." };
        }
    }

    private static object Summarize(List<Attendance> rows, DateTime from) => new
    {
        month = Local(from).ToString("yyyy-MM"),
        workDays = rows.Select(a => Local(a.CheckIn).Date).Distinct().Count(),
        lateCount = rows.Count(a => a.Status == "Late"),
        lateMinutes = rows.Sum(a => a.LateMinutes),
        earlyLeaveCount = rows.Count(a => a.EarlyLeaveMinutes > 0),
        hours = Math.Round(rows.Where(a => a.CheckOut != null).Sum(a => (a.CheckOut!.Value - a.CheckIn).TotalHours), 1),
        missingCheckout = rows.Count(a => a.CheckOut == null),
        records = rows.TakeLast(31).Select(a => new { checkIn = Fmt(a.CheckIn), checkOut = Fmt(a.CheckOut), a.Status, a.LateMinutes, a.EarlyLeaveMinutes }),
    };

    private Task<Dictionary<string, string>> Names(CancellationToken ct) =>
        db.Users.Where(u => u.CompanyId == CompanyId).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

    private async Task<List<string>> NotCheckedIn(HashSet<string> present, CancellationToken ct)
    {
        var emps = await (from u in db.Users join ur in db.UserRoles on u.Id equals ur.UserId join r in db.Roles on ur.RoleId equals r.Id
                          where u.CompanyId == CompanyId && r.Name == Roles.Employee select new { u.Id, u.FullName }).ToListAsync(ct);
        return emps.Where(e => !present.Contains(e.Id)).Select(e => e.FullName).ToList();
    }

    private static (DateTime from, DateTime to) MonthRange(string? month)
    {
        var start = DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)
            ? new DateTime(m.Year, m.Month, 1)
            : new DateTime(Local(DateTime.UtcNow).Year, Local(DateTime.UtcNow).Month, 1);
        return (DateTime.SpecifyKind(start - Vn, DateTimeKind.Utc), DateTime.SpecifyKind(start.AddMonths(1) - Vn, DateTimeKind.Utc));
    }
}
