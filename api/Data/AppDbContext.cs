using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace NguyenDuongNhutKhang_app.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Workplace> Workplaces => Set<Workplace>();
    public DbSet<Attendance> Attendances => Set<Attendance>();
    public DbSet<LocationPoint> Locations => Set<LocationPoint>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<CompanyInvite> CompanyInvites => Set<CompanyInvite>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<AppUser>().Property(u => u.FullName).HasMaxLength(120);
        b.Entity<AppUser>().Property(u => u.Position).HasMaxLength(80);
        b.Entity<AppUser>().Property(u => u.Department).HasMaxLength(80);
        b.Entity<AppUser>().HasIndex(u => u.CompanyId);
        b.Entity<Company>().Property(f => f.Name).HasMaxLength(120);
        b.Entity<Workplace>().Property(z => z.Name).HasMaxLength(120);
        b.Entity<Workplace>().Property(z => z.QrSecret).HasMaxLength(64);
        b.Entity<Workplace>().HasIndex(z => z.CompanyId);
        b.Entity<Attendance>().Property(a => a.Note).HasMaxLength(300);
        b.Entity<Attendance>().Property(a => a.Status).HasMaxLength(20);
        b.Entity<Attendance>().HasIndex(a => new { a.CompanyId, a.CheckIn });
        b.Entity<Attendance>().HasIndex(a => new { a.UserId, a.CheckIn });
        b.Entity<LocationPoint>().HasIndex(l => new { l.UserId, l.CreatedAt });
        b.Entity<ChatMessage>().Property(m => m.Text).HasMaxLength(2000);
        b.Entity<ChatMessage>().HasIndex(m => new { m.CompanyId, m.Id });
        b.Entity<CompanyInvite>().Property(x => x.Code).HasMaxLength(6);
        b.Entity<CompanyInvite>().HasIndex(x => x.Code);
        b.Entity<OtpCode>().HasIndex(o => new { o.UserId, o.Purpose });
    }
}
