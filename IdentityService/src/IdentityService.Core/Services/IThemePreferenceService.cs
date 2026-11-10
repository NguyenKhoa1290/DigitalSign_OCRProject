namespace IdentityService.Core.Services;

public interface IThemePreferenceService
{
    Task<string> SetAsync(Guid userId, string theme, CancellationToken token);
}
