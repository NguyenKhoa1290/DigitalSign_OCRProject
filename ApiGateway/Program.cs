using System.Text;
using System.Net.Http.Json;
using System.Text.Json;
using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;

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
        lc.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

    var config = builder.Configuration;

    // ── JWT Authentication (validate only) ───────────────────────────────────
    var jwtKey     = config["JwtSettings:Key"]      ?? throw new InvalidOperationException("JwtSettings:Key missing");
    var jwtIssuer  = config["JwtSettings:Issuer"]   ?? throw new InvalidOperationException("JwtSettings:Issuer missing");
    var jwtAudience = config["JwtSettings:Audience"] ?? throw new InvalidOperationException("JwtSettings:Audience missing");
    var validateTokenUrl = config["AuthValidation:ValidateTokenUrl"]
        ?? "http://localhost:5048/api/auth/validate-token";
    var validateTimeoutSeconds = int.TryParse(config["AuthValidation:TimeoutSeconds"], out var configuredTimeoutSeconds)
        ? configuredTimeoutSeconds
        : 3;
    var failOpenOnValidationError = !bool.TryParse(config["AuthValidation:FailOpenOnValidationError"], out var configuredFailOpen)
        || configuredFailOpen;

    builder.Services.AddHttpClient("identity-token-validation", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(validateTimeoutSeconds);
    });

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidIssuer              = jwtIssuer,
                ValidateAudience         = true,
                ValidAudience            = jwtAudience,
                ValidateLifetime         = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ClockSkew                = TimeSpan.FromSeconds(30),
            };

            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async ctx =>
                {
                    var authorization = ctx.Request.Headers.Authorization.ToString();
                    const string bearerPrefix = "Bearer ";
                    if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
                        return;

                    var token = authorization[bearerPrefix.Length..].Trim();
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        ctx.Fail("Token is missing.");
                        return;
                    }

                    try
                    {
                        var httpClientFactory = ctx.HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>();
                        var httpClient = httpClientFactory.CreateClient("identity-token-validation");
                        var response = await httpClient.PostAsJsonAsync(validateTokenUrl, new { token });

                        if (!response.IsSuccessStatusCode)
                        {
                            if (failOpenOnValidationError)
                            {
                                Log.Warning("IdentityService token validation returned {StatusCode}. Falling back to local JWT validation.",
                                    response.StatusCode);
                                return;
                            }

                            ctx.Fail("IdentityService token validation failed.");
                            return;
                        }

                        await using var stream = await response.Content.ReadAsStreamAsync();
                        using var json = await JsonDocument.ParseAsync(stream);
                        if (!json.RootElement.TryGetProperty("isValid", out var isValidProperty))
                        {
                            if (failOpenOnValidationError)
                            {
                                Log.Warning("IdentityService token validation response is missing isValid. Falling back to local JWT validation.");
                                return;
                            }

                            ctx.Fail("IdentityService token validation response is invalid.");
                            return;
                        }

                        if (isValidProperty.ValueKind != JsonValueKind.True)
                        {
                            ctx.Fail("Token has been revoked or is no longer valid.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Không kiểm tra được token với IdentityService.");
                        if (!failOpenOnValidationError)
                        {
                            ctx.Fail("IdentityService token validation is unavailable.");
                        }
                    }
                },
                OnChallenge = async ctx =>
                {
                    ctx.HandleResponse();
                    ctx.Response.StatusCode  = 401;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync(
                        """{"success":false,"message":"Bạn chưa đăng nhập hoặc token không hợp lệ."}""");
                },
                OnForbidden = async ctx =>
                {
                    ctx.Response.StatusCode  = 403;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync(
                        """{"success":false,"message":"Bạn không có quyền truy cập tài nguyên này."}""");
                },
            };
        });

    builder.Services.AddAuthorization();

    // ── YARP Reverse Proxy ────────────────────────────────────────────────────
    builder.Services.AddReverseProxy()
        .LoadFromConfig(config.GetSection("ReverseProxy"));

    // ── Rate Limiting ─────────────────────────────────────────────────────────
    builder.Services.AddMemoryCache();
    builder.Services.Configure<IpRateLimitOptions>(config.GetSection("IpRateLimiting"));
    builder.Services.AddInMemoryRateLimiting();
    builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

    // ── CORS ──────────────────────────────────────────────────────────────────
    builder.Services.AddCors(o =>
        o.AddDefaultPolicy(p =>
            p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

    // ── Swagger (chỉ để hiện thị thông tin Gateway) ───────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new()
        {
            Title       = "HAU API Gateway",
            Version     = "v1",
            Description = "Cổng API trung tâm — Trường ĐH Kiến Trúc Hà Nội",
        });
    });

    // ── Health Checks ─────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks();

    // ─────────────────────────────────────────────────────────────────────────
    var app = builder.Build();
    // ─────────────────────────────────────────────────────────────────────────

    app.UseSerilogRequestLogging();

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "HAU API Gateway v1");
        c.RoutePrefix = "swagger";
    });

    app.UseIpRateLimiting();
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();

    // ── Health check endpoint ─────────────────────────────────────────────────
    app.MapHealthChecks("/health");

    // ── Info endpoint ─────────────────────────────────────────────────────────
    app.MapGet("/", () => new
    {
        service  = "HAU API Gateway",
        version  = "1.0.0",
        swagger  = "/swagger",
        health   = "/health",
        services = new[]
        {
            new { name = "Identity Service", routes = new[] { "/api/auth/**", "/api/users/**", "/api/roles/**", "/api/departments/**" } },
            new { name = "Document Service", routes = new[] { "/api/documents/**" } },
            new { name = "Sign Service",     routes = new[] { "/api/signatures/**" } },
            new { name = "OCR Service",      routes = new[] { "/api/ocr/**" } },
        },
    });

    // ── YARP proxy (phải là middleware cuối cùng) ─────────────────────────────
    app.MapReverseProxy();

    Log.Information("API Gateway đang khởi động tại http://localhost:5000");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "API Gateway khởi động thất bại.");
}
finally
{
    await Log.CloseAndFlushAsync();
}
