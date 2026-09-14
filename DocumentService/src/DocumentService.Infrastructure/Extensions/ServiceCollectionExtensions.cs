using DocumentService.Core.Interfaces;
using DocumentService.Infrastructure.Data;
using DocumentService.Infrastructure.Repositories;
using DocumentService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;

namespace DocumentService.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký toàn bộ dependency của Infrastructure layer:
    /// DbContext, MinIO client, repositories và services.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── PostgreSQL / EF Core ──────────────────────────────────────────────
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsqlOptions => npgsqlOptions.CommandTimeout(30)));

        // ── MinIO client (singleton) ──────────────────────────────────────────
        services.AddSingleton<IMinioClient>(sp =>
        {
            var endpoint = configuration["MinioSettings:Endpoint"]
                ?? throw new InvalidOperationException("MinioSettings:Endpoint chưa được cấu hình.");
            var accessKey = configuration["MinioSettings:AccessKey"]
                ?? throw new InvalidOperationException("MinioSettings:AccessKey chưa được cấu hình.");
            var secretKey = configuration["MinioSettings:SecretKey"]
                ?? throw new InvalidOperationException("MinioSettings:SecretKey chưa được cấu hình.");
            var useSSL = configuration.GetValue<bool>("MinioSettings:UseSSL", false);

            var client = new MinioClient()
                .WithEndpoint(endpoint)
                .WithCredentials(accessKey, secretKey)
                .WithSSL(useSSL)
                .Build();

            return client;
        });

        // ── Repositories ──────────────────────────────────────────────────────
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IDocumentTypeRepository, DocumentTypeRepository>();
        services.AddScoped<IDocumentProcessRepository, DocumentProcessRepository>();

        // ── Services ──────────────────────────────────────────────────────────
        services.AddScoped<IFileStorageService, MinioStorageService>();
        services.AddScoped<IDocumentService, Services.DocumentService>();

        // ── Kafka Producer (Singleton — IProducer nên dùng chung) ──────────────
        services.AddSingleton<IKafkaProducerService, KafkaProducerService>();

        return services;
    }
}
