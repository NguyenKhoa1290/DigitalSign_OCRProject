using IdentityService.Core.Entities;
using IdentityService.Core.Interfaces;
using IdentityService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation của IPasswordResetRepository.
/// </summary>
public class PasswordResetRepository : IPasswordResetRepository
{
    private readonly AppDbContext _context;

    public PasswordResetRepository(AppDbContext context) => _context = context;

    public async Task<PasswordResetToken> CreateAsync(PasswordResetToken token)
    {
        await _context.PasswordResetTokens.AddAsync(token);
        await _context.SaveChangesAsync();
        return token;
    }

    public async Task<PasswordResetToken?> GetValidTokenAsync(Guid userId, string tokenHash)
        => await _context.PasswordResetTokens
            .Where(t => t.UserId    == userId
                     && t.TokenHash == tokenHash
                     && !t.IsUsed
                     && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

    public async Task InvalidateAllAsync(Guid userId)
    {
        var tokens = await _context.PasswordResetTokens
            .Where(t => t.UserId == userId && !t.IsUsed)
            .ToListAsync();

        foreach (var t in tokens)
            t.IsUsed = true;

        await _context.SaveChangesAsync();
    }
}
