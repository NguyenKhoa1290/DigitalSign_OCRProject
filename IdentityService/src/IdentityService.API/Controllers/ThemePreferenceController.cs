using System.Security.Claims;
using IdentityService.Core.Common;
using IdentityService.Core.DTOs.Users;
using IdentityService.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityService.API.Controllers;

[ApiController, Authorize, Route("api/users/me/theme")]
public sealed class ThemePreferenceController(IThemePreferenceService themes) : ControllerBase
{
    [HttpPut]
    public async Task<IActionResult> Update(ThemePreferenceDto request, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var actor))
            return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        var theme = await themes.SetAsync(actor, request.Theme, token);
        return Ok(ApiResponse<ThemePreferenceDto>.Ok(new() { Theme = theme }, "Đã lưu giao diện theo tài khoản."));
    }
}
