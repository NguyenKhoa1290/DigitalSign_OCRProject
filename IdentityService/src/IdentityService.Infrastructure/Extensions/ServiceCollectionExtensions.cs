using IdentityService.Core.Interfaces;
using IdentityService.Core.Services;
using IdentityService.Infrastructure.Data;
using IdentityService.Infrastructure.Repositories;
using IdentityService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Infrastructure.Extensions;

/// <summary>
/// Extension methods để đăng ký toàn bộ Infrastructure layer vào DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký DbContext (Npgsql/PostgreSQL), Repositories và Services.
    /// Gọi trong <c>Program.cs</c>: <c>builder.Services.AddInfrastructure(builder.Configuration);</c>
    /// </summary>
    /// <param name="services">IServiceCollection hiện tại.</param>
    /// <param name="configuration">IConfiguration để đọc connection string và JWT settings.</param>
    /// <returns>IServiceCollection để chain tiếp.</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Database ──────────────────────────────────────────────────────────
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorCodesToAdd: null);
                    npgsqlOptions.CommandTimeout(30);
                })
            .EnableSensitiveDataLogging(false)
            .EnableDetailedErrors(false)
        );

        // ── Repositories (Scoped) ───────────────────────────────────────────────────
        services.AddScoped<IUserRepository,          UserRepository>();
        services.AddScoped<IRoleRepository,          RoleRepository>();
        services.AddScoped<IDepartmentRepository,    DepartmentRepository>();
        services.AddScoped<IPasswordResetRepository, PasswordResetRepository>();

        // ── Services (Scoped) ─────────────────────────────────────────────────────────
        services.AddScoped<ITokenService,      TokenService>();
        services.AddScoped<IAuthService,       AuthService>();
        services.AddScoped<IUserService,       UserService>();
        services.AddScoped<IRoleService,       RoleService>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IEmailService,      EmailService>();

        return services;
    }
}
