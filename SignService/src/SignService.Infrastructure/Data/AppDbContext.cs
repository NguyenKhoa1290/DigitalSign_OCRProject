using Microsoft.EntityFrameworkCore;
using SignService.Core.Entities;

namespace SignService.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Signature> Signatures => Set<Signature>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Signature>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.SignatureType).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.DocId);
        });
    }
}
