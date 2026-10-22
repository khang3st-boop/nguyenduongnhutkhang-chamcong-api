using System.Text;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using NguyenDuongNhutKhang_app.Data;
using NguyenDuongNhutKhang_app.Services;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header
    });
    o.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", doc)] = []
    });
});

// Database:Provider = SqlServer (mặc định, dùng migration) hoặc Postgres (khi triển khai lên VPS miễn phí).
var usePostgres = string.Equals(cfg["Database:Provider"], "Postgres", StringComparison.OrdinalIgnoreCase);
var connStr = cfg.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Thiếu DefaultConnection.");
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (usePostgres) o.UseNpgsql(connStr);
    else o.UseSqlServer(connStr);
});
builder.Services.AddIdentity<AppUser, IdentityRole>(o =>
{
    o.Password.RequiredLength = 6;
    o.Password.RequireNonAlphanumeric = false;
    o.Password.RequireUppercase = false;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
}).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();

builder.Services.Configure<SmtpOptions>(cfg.GetSection("Smtp"));
builder.Services.Configure<BrevoOptions>(cfg.GetSection("Brevo"));
builder.Services.Configure<GoogleOptions>(cfg.GetSection("Google"));
builder.Services.Configure<AgoraOptions>(cfg.GetSection("Agora"));
builder.Services.Configure<AdminSeedOptions>(cfg.GetSection("Admin"));
builder.Services.AddScoped<EmailSender>();
builder.Services.AddScoped<OtpService>();
builder.Services.AddSingleton<JwtService>();

var jwt = cfg.GetSection("JWTKey");
var secret = jwt["Secret"];
if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
    throw new InvalidOperationException("Thiếu JWTKey:Secret (>= 32 ký tự). Dùng: dotnet user-secrets set JWTKey:Secret <chuỗi ngẫu nhiên>");
builder.Services.AddAuthentication(o =>
{
    o.DefaultAuthenticateScheme = "Bearer";
    o.DefaultChallengeScheme = "Bearer";
}).AddJwtBearer("Bearer", o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
        ValidIssuer = jwt["ValidIssuer"], ValidAudience = jwt["ValidAudience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };
    // Tài khoản bị khóa/xóa mất hiệu lực ngay cả khi token còn hạn.
    o.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
    {
        OnTokenValidated = async ctx =>
        {
            var um = ctx.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
            var id = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var u = id == null ? null : await um.FindByIdAsync(id);
            if (u == null || await um.IsLockedOutAsync(u)) ctx.Fail("Tài khoản không còn hợp lệ.");
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Tạo bảng (migration), seed vai trò và tài khoản quản trị.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Migration hiện có sinh cho SQL Server; PostgreSQL tạo bảng trực tiếp từ model.
    if (usePostgres) await db.Database.EnsureCreatedAsync();
    else await db.Database.MigrateAsync();
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var r in new[] { Roles.Admin, Roles.Parent, Roles.Child })
        if (!await roles.RoleExistsAsync(r)) await roles.CreateAsync(new IdentityRole(r));

    var seed = cfg.GetSection("Admin").Get<AdminSeedOptions>() ?? new();
    if (!string.IsNullOrWhiteSpace(seed.Email) && !string.IsNullOrWhiteSpace(seed.Password))
    {
        var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (await um.FindByNameAsync(seed.UserName) == null)
        {
            var admin = new AppUser { UserName = seed.UserName, Email = seed.Email, EmailConfirmed = true, FullName = "Quản trị viên" };
            var res = await um.CreateAsync(admin, seed.Password);
            if (res.Succeeded) await um.AddToRoleAsync(admin, Roles.Admin);
            else app.Logger.LogError("Không tạo được admin: {Errors}", string.Join("; ", res.Errors.Select(e => e.Code)));
        }
    }
}

app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError("{Method} {Path} lỗi {Type}: {Msg}", ctx.Request.Method, ctx.Request.Path, ex?.GetType().Name, ex?.Message);
    await Results.Problem(statusCode: 500, title: "Có lỗi máy chủ. Vui lòng thử lại sau.").ExecuteAsync(ctx);
}));

app.UseSwagger();
app.UseSwaggerUI();
app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
