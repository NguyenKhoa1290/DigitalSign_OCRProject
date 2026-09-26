using IdentityService.Core.Entities;

namespace IdentityService.Core.Interfaces;

public interface IEmailVerificationRepository
{
    Task<EmailVerificationToken> CreateAsync(EmailVerificationToken token);
    Task<EmailVerificationToken?> GetValidTokenAsync(Guid userId, string email, string tokenHash);
    Task InvalidateAllAsync(Guid userId);
}
