using IdentityService.Core.Entities;
using IdentityService.Core.Interfaces;
using IdentityService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Infrastructure.Repositories;

public class EmailVerificationRepository : IEmailVerificationRepository
{
    private readonly AppDbContext _context;

    public EmailVerificationRepository(AppDbContext context) => _context = context;

    public async Task<EmailVerificationToken> CreateAsync(EmailVerificationToken token)
    {
        await _context.EmailVerificationTokens.AddAsync(token);
        await _context.SaveChangesAsync();
        return token;
    }

    public async Task<EmailVerificationToken?> GetValidTokenAsync(
        Guid userId,
        string email,
        string tokenHash)
        => await _context.EmailVerificationTokens
            .Where(t => t.UserId == userId
                     && t.Email == email
                     && t.TokenHash == tokenHash
                     && !t.IsUsed
                     && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

    public async Task InvalidateAllAsync(Guid userId)
    {
        var tokens = await _context.EmailVerificationTokens
            .Where(t => t.UserId == userId && !t.IsUsed)
            .ToListAsync();

        foreach (var token in tokens)
            token.IsUsed = true;

        await _context.SaveChangesAsync();
    }
}
