namespace IdentityService.Core.Common;

/// <summary>
/// Wrapper chuẩn hóa cho tất cả các API response trong hệ thống.
/// Đảm bảo định dạng phản hồi nhất quán giữa các endpoint.
/// </summary>
/// <typeparam name="T">Kiểu dữ liệu của phần data trong response.</typeparam>
public class ApiResponse<T>
{
    /// <summary>Cho biết request có được xử lý thành công hay không.</summary>
    public bool Success { get; private set; }

    /// <summary>Thông điệp mô tả kết quả (thành công hoặc lỗi chính).</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>Dữ liệu trả về. Null trong trường hợp thất bại.</summary>
    public T? Data { get; private set; }

    /// <summary>Danh sách chi tiết lỗi (validation errors, field errors, v.v.).</summary>
    public List<string> Errors { get; private set; } = new List<string>();

    // Hàm khởi tạo private để bắt buộc dùng factory methods
    private ApiResponse() { }

    // -------------------------
    // Factory Methods
    // -------------------------

    /// <summary>
    /// Tạo response thành công với dữ liệu và thông điệp tùy chọn.
    /// </summary>
    /// <param name="data">Dữ liệu cần trả về.</param>
    /// <param name="message">Thông điệp thành công (mặc định: "Thao tác thành công.").</param>
    /// <returns>ApiResponse với Success = true.</returns>
    public static ApiResponse<T> Ok(T data, string message = "Thao tác thành công.")
    {
        return new ApiResponse<T>
        {
            Success = true,
            Message = message,
            Data = data,
            Errors = new List<string>()
        };
    }

    /// <summary>
    /// Tạo response thất bại với một thông điệp lỗi duy nhất.
    /// </summary>
    /// <param name="message">Mô tả lỗi chính.</param>
    /// <returns>ApiResponse với Success = false.</returns>
    public static ApiResponse<T> Fail(string message)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            Data = default,
            Errors = new List<string> { message }
        };
    }

    /// <summary>
    /// Tạo response thất bại với danh sách nhiều lỗi (ví dụ: validation errors).
    /// </summary>
    /// <param name="errors">Danh sách chi tiết các lỗi.</param>
    /// <param name="message">Thông điệp lỗi tổng quát (mặc định: "Yêu cầu không hợp lệ.").</param>
    /// <returns>ApiResponse với Success = false và danh sách Errors.</returns>
    public static ApiResponse<T> Fail(List<string> errors, string message = "Yêu cầu không hợp lệ.")
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            Data = default,
            Errors = errors ?? new List<string>()
        };
    }
}
