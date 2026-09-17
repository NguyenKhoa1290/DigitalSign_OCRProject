using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using SignService.Core.Interfaces;
using SignService.Infrastructure.Data;
using SignService.Infrastructure.Repositories;
using SignService.Infrastructure.Services;

namespace SignService.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // EF Core + PostgreSQL
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseNpgsql(config.GetConnectionString("DefaultConnection")));

        // MinIO
        services.AddSingleton<IMinioClient>(sp =>
        {
            var endpoint = config["MinioSettings:Endpoint"] ?? "localhost:9000";
            var accessKey = config["MinioSettings:AccessKey"] ?? "minioadmin";
            var secretKey = config["MinioSettings:SecretKey"] ?? "minioadmin";
            var useSSL = bool.Parse(config["MinioSettings:UseSSL"] ?? "false");

            var client = new MinioClient()
                .WithEndpoint(endpoint)
                .WithCredentials(accessKey, secretKey);

            if (useSSL)
                client = client.WithSSL();

            return client.Build();
        });

        // Repositories
        services.AddScoped<ISignatureRepository, SignatureRepository>();
        services.AddScoped<IDocumentFileRepository, DocumentFileRepository>();

        // Services
        services.AddSingleton<ICertificateService, CertificateService>();
        services.AddScoped<IMinioService, MinioService>();
        services.AddScoped<IPdfSigningService, PdfSigningService>();
        services.AddScoped<ISignService, SignService.Infrastructure.Services.SignService>();

        return services;
    }
}
