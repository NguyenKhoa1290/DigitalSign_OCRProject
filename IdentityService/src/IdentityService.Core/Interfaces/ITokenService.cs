using IdentityService.Core.Entities;

namespace IdentityService.Core.Interfaces;

/// <summary>
/// Giao diện dịch vụ tạo và xác thực JWT token.
/// Được triển khai tại tầng Infrastructure.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Tạo JWT Access Token cho người dùng đã xác thực.
    /// Token chứa các claims: UserId, Username, FullName, Roles.
    /// </summary>
    /// <param name="user">Thông tin người dùng để đưa vào claims.</param>
    /// <param name="roles">Danh sách tên vai trò của người dùng.</param>
    /// <returns>Chuỗi JWT Access Token đã ký.</returns>
    string GenerateAccessToken(AppUser user, IEnumerable<string> roles);

    /// <summary>
    /// Tạo Refresh Token ngẫu nhiên, an toàn mật mã học.
    /// Token này được lưu trữ và dùng để gia hạn Access Token.
    /// </summary>
    /// <returns>Chuỗi Refresh Token (Base64 URL-safe).</returns>
    string GenerateRefreshToken();

    /// <summary>
    /// Xác thực tính hợp lệ của Access Token (chữ ký, thời hạn, issuer, audience).
    /// </summary>
    /// <param name="token">Chuỗi JWT Access Token cần xác thực.</param>
    /// <returns>True nếu token hợp lệ và chưa hết hạn, False trong trường hợp ngược lại.</returns>
    Task<bool> ValidateTokenAsync(string token);
}
