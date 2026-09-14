using IdentityService.Infrastructure.Extensions;
using IdentityService.API.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;
using System.Reflection;

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
        var context = services.GetRequiredService<IdentityService.Infrastructure.Data.AppDbContext>();
        await context.Database.EnsureCreatedAsync();
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
