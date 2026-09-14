using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using SignService.Core.Exceptions;
using SignService.Core.Interfaces;
using SignService.Infrastructure.Data;
using SignService.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ──────────────────────────────────────────────────────────────────────────────
// Serilog
// ──────────────────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// ──────────────────────────────────────────────────────────────────────────────
// Infrastructure
// ──────────────────────────────────────────────────────────────────────────────
builder.Services.AddInfrastructure(builder.Configuration);

// ──────────────────────────────────────────────────────────────────────────────
// JWT Authentication (validate only — IdentityService issues tokens)
// ──────────────────────────────────────────────────────────────────────────────
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]!);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(key)
        };
    });

builder.Services.AddAuthorization();

// ──────────────────────────────────────────────────────────────────────────────
// Controllers + Swagger
// ──────────────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "HAU SignService API",
        Version = "v1",
        Description = "Dịch vụ ký số PDF — PKI nội bộ"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập JWT token: Bearer {token}"
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
});

// ──────────────────────────────────────────────────────────────────────────────
// Build
// ──────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ──────────────────────────────────────────────────────────────────────────────
// Initialize Root CA + run DB migrations
// ──────────────────────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    // Run EF migrations automatically
    try
    {
        var db = services.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        Log.Information("Database migration completed.");
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Database migration failed (may already be up to date): {Message}", ex.Message);
    }

    // Initialize PKI Root CA
    try
    {
        var certService = services.GetRequiredService<ICertificateService>();
        await certService.InitializeRootCaAsync();
        Log.Information("Root CA initialized.");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Root CA initialization failed.");
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Global Exception Handler
// ──────────────────────────────────────────────────────────────────────────────
app.UseExceptionHandler(errApp =>
{
    errApp.Run(async ctx =>
    {
        var feature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var ex = feature?.Error;

        if (ex == null) return;

        Log.Error(ex, "Unhandled exception: {Message}", ex.Message);

        (int statusCode, string message) = ex switch
        {
            DocumentNotFoundException => (StatusCodes.Status404NotFound, ex.Message),
            CertificateNotFoundException => (StatusCodes.Status404NotFound, ex.Message),
            CertificateExpiredException => (StatusCodes.Status422UnprocessableEntity, ex.Message),
            AlreadySignedException => (StatusCodes.Status409Conflict, ex.Message),
            PdfSigningException => (StatusCodes.Status500InternalServerError, ex.Message),
            SignServiceException => (StatusCodes.Status400BadRequest, ex.Message),
            _ => (StatusCodes.Status500InternalServerError, "Lỗi hệ thống nội bộ.")
        };

        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "application/json";

        var response = new
        {
            success = false,
            message,
            errors = new List<string>()
        };

        await ctx.Response.WriteAsJsonAsync(response);
    });
});

// ──────────────────────────────────────────────────────────────────────────────
// Middleware pipeline
// ──────────────────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "HAU SignService v1");
        c.RoutePrefix = string.Empty;
    });
}

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

Log.Information("SignService starting on port 5050...");
await app.RunAsync();
