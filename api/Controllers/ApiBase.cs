using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace NguyenDuongNhutKhang_app.Controllers;

[ApiController]
public abstract class ApiBase : ControllerBase
{
    protected string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    protected string Role => User.FindFirstValue(ClaimTypes.Role) ?? "";
    protected int CompanyId => int.TryParse(User.FindFirstValue("company_id"), out var f) ? f : 0;

    protected IActionResult Fail(int status, string message) =>
        StatusCode(status, new { status = false, message });
}
