using IdentityService.Core.Exceptions;
using IdentityService.Core.Services;
using IdentityService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Infrastructure.Services;

public sealed class ThemePreferenceService(AppDbContext db) : IThemePreferenceService
{
    public async Task<string> SetAsync(Guid userId, string theme, CancellationToken token)
    {
        if (theme is not ("light" or "dark"))
            throw new IdentityServiceException("Chỉ chọn giao diện light hoặc dark.", 400);
        var user = await db.AppUsers.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, token)
            ?? throw new UserNotFoundException(userId);
        // Track only this property; never overwrite profile, roles or credentials.
        user.Theme = theme;
        await db.SaveChangesAsync(token);
        return user.Theme;
    }
}
