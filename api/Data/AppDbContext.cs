using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace NguyenDuongNhutKhang_app.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Family> Families => Set<Family>();
    public DbSet<SafeZone> SafeZones => Set<SafeZone>();
    public DbSet<LocationPoint> Locations => Set<LocationPoint>();
    public DbSet<ZoneState> ZoneStates => Set<ZoneState>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();
    public DbSet<TranslationRecord> Translations => Set<TranslationRecord>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<BlockedSite> BlockedSites => Set<BlockedSite>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<AppUser>().Property(u => u.FullName).HasMaxLength(120);
        b.Entity<AppUser>().HasIndex(u => u.FamilyId);
        b.Entity<Family>().Property(f => f.Name).HasMaxLength(120);
        b.Entity<SafeZone>().Property(z => z.Name).HasMaxLength(120);
        b.Entity<SafeZone>().HasIndex(z => z.FamilyId);
        b.Entity<LocationPoint>().HasIndex(l => new { l.UserId, l.CreatedAt });
        b.Entity<ZoneState>().HasKey(s => new { s.UserId, s.ZoneId });
        b.Entity<Alert>().Property(a => a.Message).HasMaxLength(500);
        b.Entity<Alert>().Property(a => a.ImageUrl).HasMaxLength(300);
        b.Entity<Alert>().HasIndex(a => new { a.FamilyId, a.CreatedAt });
        b.Entity<ChatMessage>().Property(m => m.Text).HasMaxLength(2000);
        b.Entity<ChatMessage>().HasIndex(m => new { m.FamilyId, m.Id });
        b.Entity<TranslationRecord>().Property(t => t.SourceText).HasMaxLength(4000);
        b.Entity<TranslationRecord>().Property(t => t.TranslatedText).HasMaxLength(4000);
        b.Entity<TranslationRecord>().HasIndex(t => new { t.FamilyId, t.CreatedAt });
        b.Entity<BlockedSite>().Property(x => x.Domain).HasMaxLength(200);
        b.Entity<BlockedSite>().HasIndex(x => new { x.FamilyId, x.ChildId });
        b.Entity<OtpCode>().HasIndex(o => new { o.UserId, o.Purpose });
    }
}
