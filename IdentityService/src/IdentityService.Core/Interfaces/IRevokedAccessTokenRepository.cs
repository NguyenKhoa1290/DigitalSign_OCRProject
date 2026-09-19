using IdentityService.Core.Entities;

namespace IdentityService.Core.Interfaces;

public interface IRevokedAccessTokenRepository
{
    Task AddAsync(RevokedAccessToken token);
    Task<bool> IsRevokedAsync(string jti);
}
