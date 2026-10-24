using Microsoft.EntityFrameworkCore;
using SignService.Core.Entities;

namespace SignService.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Signature> Signatures => Set<Signature>();
    public DbSet<DocumentFileRecord> DocumentFiles => Set<DocumentFileRecord>();
    public DbSet<IncomingProcessRecord> IncomingProcesses => Set<IncomingProcessRecord>();
    public DbSet<DocumentTypeRecord> DocumentTypes => Set<DocumentTypeRecord>();

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
            e.Property(x => x.Status).HasMaxLength(50);
            e.Property(x => x.ProcessingDepartmentName).HasMaxLength(255);
            e.Property(x => x.AssignedToName).HasMaxLength(100);
            e.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
            e.HasQueryFilter(x => !x.IsDeleted);
        });
        modelBuilder.Entity<IncomingProcessRecord>(e =>
        {
            e.ToTable("DocumentProcesses", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(50);
        });
        modelBuilder.Entity<DocumentTypeRecord>(e =>
        {
            e.ToTable("DocumentTypes", t => t.ExcludeFromMigrations());
            e.HasKey(x => x.Id);
            e.Property(x => x.TypeName).HasMaxLength(100);
        });
    }
}
