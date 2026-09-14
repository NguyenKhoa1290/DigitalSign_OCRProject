using System.Net.Http.Json;
using System.Security.Claims;
using Blazored.LocalStorage;
using HauDocumentApp.Auth;
using HauDocumentApp.Models;

namespace HauDocumentApp.Services;

public class AuthService
{
    private readonly HttpClient               _httpClient;
    private readonly ILocalStorageService     _localStorage;
    private readonly CustomAuthStateProvider  _authStateProvider;
    private const string TokenKey             = "auth_token";
    private const string MustChangeKey        = "must_change_password";

    public AuthService(
        HttpClient              httpClient,
        ILocalStorageService    localStorage,
        CustomAuthStateProvider authStateProvider)
    {
        _httpClient        = httpClient;
        _localStorage      = localStorage;
        _authStateProvider = authStateProvider;
    }

    // ── Login ──────────────────────────────────────────────────────────────────

    /// <summary>Returns (Success, MustChangePassword, Error)</summary>
    public async Task<(bool Success, bool MustChangePassword, string? Error)> LoginAsync(string username, string password)
    {
        try
        {
            var request  = new LoginRequest { Username = username, Password = password };
            var response = await _httpClient.PostAsJsonAsync("api/auth/login", request);

            if (!response.IsSuccessStatusCode)
                return (false, false, "Tên đăng nhập hoặc mật khẩu không đúng");

            // Backend trả về LoginResponseDto trực tiếp (không có wrapper ApiResponse)
            var login = await response.Content.ReadFromJsonAsync<LoginResponse>();

            if (login == null || string.IsNullOrEmpty(login.AccessToken))
                return (false, false, "Phản hồi không hợp lệ từ server");

            await _localStorage.SetItemAsStringAsync(TokenKey, login.AccessToken);
            _authStateProvider.NotifyUserAuthenticated(login.AccessToken);

            if (login.MustChangePassword)
                await _localStorage.SetItemAsStringAsync(MustChangeKey, "true");
            else
                await _localStorage.RemoveItemAsync(MustChangeKey);

            return (true, login.MustChangePassword, null);
        }
        catch (Exception ex)
        {
            return (false, false, $"Lỗi kết nối: {ex.Message}");
        }
    }

    // ── Change Password ────────────────────────────────────────────────────────

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(ChangePasswordRequest req)
    {
        try
        {
            var token    = await GetTokenAsync();
            var request  = new HttpRequestMessage(HttpMethod.Post, "api/auth/change-password");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(req);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                return (false, ParseError(err));
            }

            await _localStorage.RemoveItemAsync(MustChangeKey);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi: {ex.Message}");
        }
    }

    // ── Forgot Password ────────────────────────────────────────────────────────

    public async Task<(bool Success, string? Error)> ForgotPasswordAsync(string email)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/auth/forgot-password",
                new ForgotPasswordRequest { Email = email });
            return (true, null); // Luôn thành công (tránh user enumeration)
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi: {ex.Message}");
        }
    }

    // ── Reset Password ─────────────────────────────────────────────────────────

    public async Task<(bool Success, string? Error)> ResetPasswordAsync(ResetPasswordRequest req)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/auth/reset-password", req);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                return (false, ParseError(err));
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi: {ex.Message}");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    public async Task LogoutAsync()
    {
        await _localStorage.RemoveItemAsync(TokenKey);
        await _localStorage.RemoveItemAsync(MustChangeKey);
        _authStateProvider.NotifyUserLoggedOut();
    }

    public async Task<string?> GetTokenAsync()
        => await _localStorage.GetItemAsStringAsync(TokenKey);

    public async Task<bool> IsAuthenticatedAsync()
    {
        var token = await GetTokenAsync();
        return !string.IsNullOrEmpty(token);
    }

    public async Task<bool> MustChangePasswordAsync()
    {
        var flag = await _localStorage.GetItemAsStringAsync(MustChangeKey);
        return flag == "true";
    }

    public async Task<ClaimsPrincipal?> GetCurrentUserAsync()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        return authState.User.Identity?.IsAuthenticated == true ? authState.User : null;
    }

    public async Task<string?> GetRoleAsync()
    {
        var user = await GetCurrentUserAsync();
        return user?.FindFirst(ClaimTypes.Role)?.Value;
    }

    public async Task<string?> GetUserNameAsync()
    {
        var user = await GetCurrentUserAsync();
        return user?.FindFirst(ClaimTypes.Name)?.Value
            ?? user?.FindFirst("name")?.Value
            ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    public async Task<string?> GetUserIdAsync()
    {
        var user = await GetCurrentUserAsync();
        return user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user?.FindFirst("sub")?.Value;
    }

    private static string ParseError(string raw)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "Có lỗi xảy ra.";
        }
        catch { }
        return "Có lỗi xảy ra. Vui lòng thử lại.";
    }
}
