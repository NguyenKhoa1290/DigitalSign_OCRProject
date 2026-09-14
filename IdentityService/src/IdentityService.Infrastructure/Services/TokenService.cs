using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IdentityService.Core.Entities;
using IdentityService.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Infrastructure.Services;

/// <summary>
/// Dịch vụ tạo và xác thực JSON Web Tokens (JWT).
/// </summary>
public class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;

    public TokenService(IConfiguration configuration) => _configuration = configuration;

    /// <summary>
    /// Tạo JWT Access Token với claims: sub, jti, username, email, roles.
    /// </summary>
    public string GenerateAccessToken(AppUser user, IEnumerable<string> roles)
    {
        var jwt = _configuration.GetSection("JwtSettings");
        var secretKey  = jwt["Key"] ?? throw new InvalidOperationException("JwtSettings:Key is not configured.");
        var issuer     = jwt["Issuer"]   ?? "IdentityService";
        var audience   = jwt["Audience"] ?? "HAU-MicroservicesClients";
        var expiryMins = int.TryParse(jwt["ExpiryMinutes"], out var m) ? m : 60;

        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,            user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti,            Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
            new(ClaimTypes.NameIdentifier,              user.Id.ToString()),
            new("username",                             user.Username),
            new(ClaimTypes.Name,                        user.FullName),
        };

        if (!string.IsNullOrEmpty(user.Email))
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var token = new JwtSecurityToken(
            issuer:             issuer,
            audience:           audience,
            claims:             claims,
            notBefore:          DateTime.UtcNow,
            expires:            DateTime.UtcNow.AddMinutes(expiryMins),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Tạo Refresh Token ngẫu nhiên an toàn về mật mã học (32 bytes).
    /// </summary>
    public string GenerateRefreshToken()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Xác thực JWT: kiểm tra chữ ký, issuer, audience và thời hạn.
    /// </summary>
    public async Task<bool> ValidateTokenAsync(string token)
    {
        var jwt = _configuration.GetSection("JwtSettings");
        var secretKey = jwt["Key"] ?? throw new InvalidOperationException("JwtSettings:Key is not configured.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = key,
            ValidateIssuer           = true,
            ValidIssuer              = jwt["Issuer"] ?? "IdentityService",
            ValidateAudience         = true,
            ValidAudience            = jwt["Audience"] ?? "HAU-MicroservicesClients",
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.Zero
        };

        try
        {
            new JwtSecurityTokenHandler().ValidateToken(token, validationParams, out _);
            return await Task.FromResult(true);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Lấy ClaimsPrincipal từ token đã hết hạn (dùng khi refresh token).
    /// </summary>
    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var jwt = _configuration.GetSection("JwtSettings");
        var secretKey = jwt["Key"] ?? throw new InvalidOperationException("JwtSettings:Key is not configured.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = key,
            ValidateIssuer           = false,
            ValidateAudience         = false,
            ValidateLifetime         = false,   // ignore expiry
            ClockSkew                = TimeSpan.Zero
        };

        try
        {
            var handler   = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, validationParams, out var securityToken);

            if (securityToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
                return null;

            return principal;
        }
        catch
        {
            return null;
        }
    }
}
