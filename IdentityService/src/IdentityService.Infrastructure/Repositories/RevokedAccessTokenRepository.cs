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
}
