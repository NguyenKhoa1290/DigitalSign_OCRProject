using IdentityService.Core.Entities;
using IdentityService.Core.Interfaces;
using IdentityService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Infrastructure.Repositories;

public class RevokedAccessTokenRepository : IRevokedAccessTokenRepository
{
    private readonly AppDbContext _context;

    public RevokedAccessTokenRepository(AppDbContext context) => _context = context;

    public async Task AddAsync(RevokedAccessToken token)
    {
        var exists = await _context.RevokedAccessTokens.AnyAsync(t => t.Jti == token.Jti);
        if (exists) return;

        await _context.RevokedAccessTokens.AddAsync(token);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> IsRevokedAsync(string jti)
        => await _context.RevokedAccessTokens
            .AnyAsync(t => t.Jti == jti && t.ExpiresAt > DateTime.UtcNow);

    public async Task RevokeAllForUserAsync(Guid userId)
    {
        var now = DateTime.UtcNow;
        var tokens = await _context.RefreshTokens
            .Where(t => t.UserId == userId && t.ExpiresAt > now)
            .Select(t => new { t.AccessTokenJti, t.ExpiresAt })
            .ToListAsync();
        var existing = (await _context.RevokedAccessTokens
            .Where(t => t.UserId == userId)
            .Select(t => t.Jti)
            .ToListAsync()).ToHashSet(StringComparer.Ordinal);

        foreach (var token in tokens.Where(t => existing.Add(t.AccessTokenJti)))
        {
            _context.RevokedAccessTokens.Add(new RevokedAccessToken
            {
                UserId = userId,
                Jti = token.AccessTokenJti,
                ExpiresAt = token.ExpiresAt,
                RevokedAt = now
            });
        }

        await _context.SaveChangesAsync();
    }
}
