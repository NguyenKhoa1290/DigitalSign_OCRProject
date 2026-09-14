using DocumentService.Core.Common;
using DocumentService.Core.Exceptions;
using DocumentService.Infrastructure.Data;
using DocumentService.Infrastructure.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;


// ─────────────────────────────────────────────────────────────────────────────
// Bootstrap Serilog
// ─────────────────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ───────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, lc) =>
        lc.ReadFrom.Configuration(ctx.Configuration)
          .WriteTo.Console());

    var configuration = builder.Configuration;

    // ── Infrastructure (DbContext, MinIO, repositories, services) ─────────────
    builder.Services.AddInfrastructure(configuration);

    // ── JWT Authentication (validate only, do not issue) ─────────────────────
    var jwtKey = configuration["JwtSettings:Key"]
        ?? throw new InvalidOperationException("JwtSettings:Key chưa được cấu hình.");
    var jwtIssuer = configuration["JwtSettings:Issuer"]
        ?? throw new InvalidOperationException("JwtSettings:Issuer chưa được cấu hình.");
    var jwtAudience = configuration["JwtSettings:Audience"]
        ?? throw new InvalidOperationException("JwtSettings:Audience chưa được cấu hình.");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,
                ValidateAudience = true,
                ValidAudience = jwtAudience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtKey)),
                ClockSkew = TimeSpan.FromSeconds(30)
            };

            options.Events = new JwtBearerEvents
            {
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";
                    var result = ApiResponse<object>.Fail("Bạn chưa đăng nhập hoặc token không hợp lệ.");
                    await context.Response.WriteAsJsonAsync(result);
                },
                OnForbidden = async context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    var result = ApiResponse<object>.Fail("Bạn không có quyền truy cập tài nguyên này.");
                    await context.Response.WriteAsJsonAsync(result);
                }
            };
        });

    builder.Services.AddAuthorization();

    // ── Controllers ───────────────────────────────────────────────────────────
    builder.Services.AddControllers();

    // ── Swagger / OpenAPI ─────────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "DocumentService API",
            Version = "v1",
            Description = "Microservice quản lý công văn - Trường ĐH Kiến Trúc Hà Nội"
        });

        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Nhập JWT token. Ví dụ: Bearer {token}"
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    // ── CORS ──────────────────────────────────────────────────────────────────
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader());
    });

    // ─────────────────────────────────────────────────────────────────────────
    // Build & configure pipeline
    // ─────────────────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ── Auto-migrate on startup ───────────────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
            Log.Information("Database migration hoàn thành.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Không thể thực hiện migration database: {Message}", ex.Message);
        }
    }

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "DocumentService API v1");
        c.RoutePrefix = string.Empty; // Swagger UI tại /
    });

    app.UseSerilogRequestLogging();
    app.UseCors();
    app.UseHttpsRedirection();

    // ── Global exception handler ──────────────────────────────────────────────
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var exceptionHandlerFeature = context.Features
                .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();

            if (exceptionHandlerFeature?.Error is not null)
            {
                var ex = exceptionHandlerFeature.Error;
                context.Response.ContentType = "application/json";

                (int statusCode, string message) = ex switch
                {
                    DocumentNotFoundException => (404, ex.Message),
                    InvalidWorkflowTransitionException => (422, ex.Message),
                    UnauthorizedDocumentAccessException => (403, ex.Message),
                    _ => (500, "Đã xảy ra lỗi nội bộ. Vui lòng thử lại sau.")
                };

                context.Response.StatusCode = statusCode;
                await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(message));

                if (statusCode == 500)
                    Log.Error(ex, "Unhandled exception");
            }
        });
    });

    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    Log.Information("DocumentService đang khởi động...");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "DocumentService khởi động thất bại.");
}
finally
{
    await Log.CloseAndFlushAsync();
}
