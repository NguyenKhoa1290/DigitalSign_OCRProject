using System.Security.Claims;
using System.Text.Json;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;

namespace HauDocumentApp.Auth;

public class CustomAuthStateProvider : AuthenticationStateProvider
{
    private readonly ILocalStorageService _localStorage;
    private readonly AuthenticationState _anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private const string TokenKey = "auth_token";

    public CustomAuthStateProvider(ILocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var token = await _localStorage.GetItemAsStringAsync(TokenKey);
            if (string.IsNullOrWhiteSpace(token))
                return _anonymous;

            var claims = ParseClaimsFromJwt(token);
            if (!claims.Any())
                return _anonymous;

            var expClaim = claims.FirstOrDefault(c => c.Type == "exp");
            if (expClaim != null && long.TryParse(expClaim.Value, out var expSeconds))
            {
                var expiry = DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime;
                if (expiry < DateTime.UtcNow)
                {
                    await _localStorage.RemoveItemAsync(TokenKey);
                    return _anonymous;
                }
            }

            var identity = new ClaimsIdentity(claims, "jwt");
            var user = new ClaimsPrincipal(identity);
            return new AuthenticationState(user);
        }
        catch
        {
            return _anonymous;
        }
    }

    public void NotifyUserAuthenticated(string token)
    {
        var claims = ParseClaimsFromJwt(token);
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
    }

    public void NotifyUserLoggedOut()
    {
        NotifyAuthenticationStateChanged(Task.FromResult(_anonymous));
    }

    private static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
    {
        var claims = new List<Claim>();
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return claims;

            var payload = parts[1];
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }
            payload = payload.Replace('-', '+').Replace('_', '/');

            var jsonBytes = Convert.FromBase64String(payload);
            var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonBytes);

            if (keyValuePairs == null) return claims;

            foreach (var kvp in keyValuePairs)
            {
                switch (kvp.Key)
                {
                    case "role":
                    case "http://schemas.microsoft.com/ws/2008/06/identity/claims/role":
                        if (kvp.Value.ValueKind == JsonValueKind.Array)
                            foreach (var role in kvp.Value.EnumerateArray())
                                claims.Add(new Claim(ClaimTypes.Role, role.GetString() ?? ""));
                        else
                            claims.Add(new Claim(ClaimTypes.Role, kvp.Value.GetString() ?? ""));
                        break;
                    case "sub":
                    case "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier":
                        claims.Add(new Claim(ClaimTypes.NameIdentifier, kvp.Value.GetString() ?? ""));
                        break;
                    case "name":
                    case "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name":
                        claims.Add(new Claim(ClaimTypes.Name, kvp.Value.GetString() ?? ""));
                        break;
                    case "email":
                        claims.Add(new Claim(ClaimTypes.Email, kvp.Value.GetString() ?? ""));
                        break;
                    default:
                        var val = kvp.Value.ValueKind == JsonValueKind.String
                            ? kvp.Value.GetString() ?? ""
                            : kvp.Value.ToString();
                        claims.Add(new Claim(kvp.Key, val));
                        break;
                }
            }
        }
        catch { }
        return claims;
    }
}
