using IdentityService.Infrastructure.Extensions;
using IdentityService.API.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;
using System.Reflection;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using IdentityService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// Serilog Logging
// ============================================================
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// ============================================================
// JWT Authentication
// ============================================================
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["Key"] ?? throw new InvalidOperationException("JWT Key is not configured");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var jti = context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            if (string.IsNullOrWhiteSpace(jti))
                return;

            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var isRevoked = await db.RevokedAccessTokens
                .AnyAsync(t => t.Jti == jti && t.ExpiresAt > DateTime.UtcNow);

            if (isRevoked)
            {
                context.Fail("Token has been revoked.");
                return;
            }

            var userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (!Guid.TryParse(userId, out var parsedUserId))
            {
                context.Fail("User ID is missing.");
                return;
            }

            var user = await db.AppUsers.FindAsync(parsedUserId);
            if (user is null || !user.IsActive)
            {
                context.Fail("Account is inactive or missing.");
                return;
            }

            var path = context.Request.Path.Value;
            var isOnboardingEndpoint = path is not null &&
                (path.Equals("/api/auth/send-email-verification", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/api/auth/change-password", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase));
            if (user.MustChangePassword && !isOnboardingEndpoint)
                context.Fail("Complete first login before accessing the application.");
        },
        OnAuthenticationFailed = context =>
        {
            Log.Warning("Authentication failed: {Error}", context.Exception.Message);
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// ============================================================
// Infrastructure Layer (DbContext, Repositories, Services)
// ============================================================
builder.Services.AddInfrastructure(builder.Configuration);

var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"];
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("HAU.DigitalSign");
if (!string.IsNullOrWhiteSpace(dataProtectionPath))
{
    Directory.CreateDirectory(dataProtectionPath);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
}

builder.Services.AddHttpClient("gemini-key-validation", client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
});

// ============================================================
// Controllers + FluentValidation
// ============================================================
builder.Services.AddControllers();

// ============================================================
// Swagger / OpenAPI
// ============================================================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Identity Service API",
        Version = "v1",
        Description = "Dịch vụ xác thực và phân quyền (RBAC) cho Hệ thống Quản lý Công văn - Trường ĐH Kiến Trúc Hà Nội",
        Contact = new OpenApiContact { Name = "HAU Dev Team" }
    });

    // JWT Bearer Auth in Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Format: **Bearer {token}**",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });

    // XML comments
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath)) c.IncludeXmlComments(xmlPath);
});

// ============================================================
// CORS
// ============================================================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// ============================================================
// Health Checks
// ============================================================
builder.Services.AddHealthChecks();

var app = builder.Build();

// ============================================================
// Middleware Pipeline
// ============================================================
app.UseExceptionHandling(); // Global Exception Handler (must be first)

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Docker"))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Identity Service v1");
        c.RoutePrefix = string.Empty; // Swagger at root
        c.DocumentTitle = "Identity Service - HAU";
    });
}

app.UseSerilogRequestLogging();
app.UseCors("AllowAll");
// app.UseHttpsRedirection(); // Tắt khi chạy local HTTP — bật lại khi deploy production HTTPS
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

// ============================================================
// Auto migrate and seed database on startup (skip in Testing env)
// ============================================================
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        await context.Database.EnsureCreatedAsync();
        await AuthStoreInitializer.EnsureAuthTablesAsync(context);
        Log.Information("Database initialized successfully");
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Failed to initialize database");
        throw;
    }
}

Log.Information("Identity Service started on {Environment}", app.Environment.EnvironmentName);
await app.RunAsync();

// Make Program accessible for integration tests
public partial class Program { }
