using Microsoft.EntityFrameworkCore;
using DocumentService.Core.Entities;

namespace DocumentService.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();
    public DbSet<DocumentProcess> DocumentProcesses => Set<DocumentProcess>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Document ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Document>(e =>
        {
            e.HasKey(x => x.Id);

            e.Property(x => x.DocNumber)
             .HasMaxLength(50);

            e.HasIndex(x => x.DocNumber)
             .HasDatabaseName("IX_Documents_DocNumber");

            e.Property(x => x.Title)
             .IsRequired()
             .HasMaxLength(500);

            e.Property(x => x.MinioPath)
             .IsRequired();

            e.Property(x => x.Status)
             .IsRequired()
             .HasMaxLength(50);

            e.Property(x => x.OcrDataRaw)
             .HasColumnType("jsonb");

            e.HasOne(x => x.DocType)
             .WithMany(t => t.Documents)
             .HasForeignKey(x => x.DocTypeId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Processes)
             .WithOne(p => p.Document)
             .HasForeignKey(p => p.DocId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── DocumentType ──────────────────────────────────────────────────────
        modelBuilder.Entity<DocumentType>(e =>
        {
            e.HasKey(x => x.Id);

            e.Property(x => x.TypeName)
             .IsRequired()
             .HasMaxLength(100);

            e.HasIndex(x => x.TypeName)
             .IsUnique()
             .HasDatabaseName("IX_DocumentTypes_TypeName");

            e.Property(x => x.Description)
             .HasMaxLength(255);
        });

        // ── DocumentProcess ───────────────────────────────────────────────────
        modelBuilder.Entity<DocumentProcess>(e =>
        {
            e.HasKey(x => x.Id);

            e.Property(x => x.Action)
             .IsRequired()
             .HasMaxLength(50);
        });

        // ── Seed DocumentTypes ────────────────────────────────────────────────
        modelBuilder.Entity<DocumentType>().HasData(
            new DocumentType
            {
                Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
                TypeName = "CongVanDen",
                Description = "Công văn đến từ bên ngoài"
            },
            new DocumentType
            {
                Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
                TypeName = "CongVanDi",
                Description = "Công văn đi ra bên ngoài"
            },
            new DocumentType
            {
                Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"),
                TypeName = "ToTrinh",
                Description = "Tờ trình nội bộ"
            },
            new DocumentType
            {
                Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004"),
                TypeName = "QuyetDinh",
                Description = "Quyết định"
            }
        );
    }
}
