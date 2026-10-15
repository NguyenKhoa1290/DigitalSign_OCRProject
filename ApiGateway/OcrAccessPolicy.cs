using Microsoft.AspNetCore.Authorization;

namespace ApiGateway;

public static class OcrAccessPolicy
{
    public static void Configure(AuthorizationOptions options) =>
        options.AddPolicy("ocrOperator", policy => policy.RequireAuthenticatedUser().RequireRole("Admin", "Clerk"));
}
