namespace IdentityService.Core.Exceptions;

// ============================================================
// Base Exception
// ============================================================

/// <summary>
/// Exception gốc cho tất cả các lỗi nghiệp vụ trong IdentityService.
/// Kế thừa từ Exception để cho phép bắt chung tại middleware.
/// </summary>
public class IdentityServiceException : Exception
{
    /// <summary>HTTP status code gợi ý cho lỗi này (để middleware sử dụng).</summary>
    public int StatusCode { get; }

    /// <summary>Khởi tạo IdentityServiceException với thông điệp và status code.</summary>
    /// <param name="message">Mô tả lỗi.</param>
    /// <param name="statusCode">HTTP status code (mặc định: 500).</param>
    public IdentityServiceException(string message, int statusCode = 500)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>Khởi tạo IdentityServiceException với thông điệp, inner exception và status code.</summary>
    public IdentityServiceException(string message, Exception innerException, int statusCode = 500)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

// ============================================================
// User Exceptions
// ============================================================

/// <summary>
/// Ném ra khi không tìm thấy người dùng theo ID, username hoặc email.
/// HTTP 404 Not Found.
/// </summary>
public class UserNotFoundException : IdentityServiceException
{
    public UserNotFoundException(string identifier)
        : base($"Không tìm thấy người dùng: '{identifier}'.", 404)
    {
    }

    public UserNotFoundException(Guid userId)
        : base($"Không tìm thấy người dùng với ID: '{userId}'.", 404)
    {
    }
}

/// <summary>
/// Ném ra khi thông tin đăng nhập (username/password) không chính xác.
/// HTTP 401 Unauthorized.
/// </summary>
public class InvalidCredentialsException : IdentityServiceException
{
    public InvalidCredentialsException()
        : base("Tên đăng nhập hoặc mật khẩu không chính xác.", 401)
    {
    }

    public InvalidCredentialsException(string message)
        : base(message, 401)
    {
    }
}

/// <summary>
/// Ném ra khi cố gắng tạo người dùng với username hoặc email đã tồn tại.
/// HTTP 409 Conflict.
/// </summary>
public class UserAlreadyExistsException : IdentityServiceException
{
    public UserAlreadyExistsException(string field, string value)
        : base($"Người dùng với {field} '{value}' đã tồn tại trong hệ thống.", 409)
    {
    }
}

/// <summary>
/// Ném ra khi tài khoản người dùng bị khóa (IsActive = false).
/// HTTP 403 Forbidden.
/// </summary>
public class AccountLockedException : IdentityServiceException
{
    public AccountLockedException(string username)
        : base($"Tài khoản '{username}' đã bị khóa. Vui lòng liên hệ quản trị viên.", 403)
    {
    }
}

// ============================================================
// Role Exceptions
// ============================================================

/// <summary>
/// Ném ra khi không tìm thấy vai trò theo ID hoặc tên.
/// HTTP 404 Not Found.
/// </summary>
public class RoleNotFoundException : IdentityServiceException
{
    public RoleNotFoundException(string identifier)
        : base($"Không tìm thấy vai trò: '{identifier}'.", 404)
    {
    }

    public RoleNotFoundException(Guid roleId)
        : base($"Không tìm thấy vai trò với ID: '{roleId}'.", 404)
    {
    }
}

// ============================================================
// Department Exceptions
// ============================================================

/// <summary>
/// Ném ra khi không tìm thấy phòng/khoa theo ID.
/// HTTP 404 Not Found.
/// </summary>
public class DepartmentNotFoundException : IdentityServiceException
{
    public DepartmentNotFoundException(Guid departmentId)
        : base($"Không tìm thấy phòng/khoa với ID: '{departmentId}'.", 404)
    {
    }

    public DepartmentNotFoundException(string identifier)
        : base($"Không tìm thấy phòng/khoa: '{identifier}'.", 404)
    {
    }
}

// ============================================================
// Token Exceptions
// ============================================================

/// <summary>
/// Ném ra khi token đã hết hạn hoặc không còn hợp lệ.
/// HTTP 401 Unauthorized.
/// </summary>
public class TokenExpiredException : IdentityServiceException
{
    public TokenExpiredException()
        : base("Token đã hết hạn. Vui lòng đăng nhập lại hoặc làm mới token.", 401)
    {
    }

    public TokenExpiredException(string message)
        : base(message, 401)
    {
    }
}
