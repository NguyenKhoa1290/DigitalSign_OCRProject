using Microsoft.EntityFrameworkCore;
using SignService.Core.Entities;

namespace SignService.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Signature> Signatures => Set<Signature>();
    public DbSet<DocumentFileRecord> DocumentFiles => Set<DocumentFileRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Signature>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SignatureType).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.DocId);
        });

        modelBuilder.Entity<DocumentFileRecord>(e =>
        {
            e.ToTable("Documents", table => table.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.MinioPath).IsRequired();
        });
    }
}
