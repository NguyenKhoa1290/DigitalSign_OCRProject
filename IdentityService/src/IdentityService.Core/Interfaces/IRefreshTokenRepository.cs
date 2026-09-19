using IdentityService.Core.Entities;

namespace IdentityService.Core.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken> CreateAsync(RefreshToken token);
    Task<RefreshToken?> GetActiveByHashAsync(Guid userId, string tokenHash);
    Task RevokeAsync(RefreshToken token, string? replacedByTokenHash = null);
    Task RevokeAllForUserAsync(Guid userId);
}
