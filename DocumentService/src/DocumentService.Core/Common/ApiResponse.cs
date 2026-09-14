namespace DocumentService.Core.Common;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
    public IEnumerable<string>? Errors { get; set; }

    private ApiResponse() { }

    public static ApiResponse<T> Ok(T data, string? message = null) =>
        new ApiResponse<T>
        {
            Success = true,
            Message = message,
            Data = data,
            Errors = null
        };

    public static ApiResponse<T> Fail(string message) =>
        new ApiResponse<T>
        {
            Success = false,
            Message = message,
            Data = default,
            Errors = null
        };

    public static ApiResponse<T> Fail(IEnumerable<string> errors) =>
        new ApiResponse<T>
        {
            Success = false,
            Message = "Yêu cầu không hợp lệ.",
            Data = default,
            Errors = errors
        };
}

/// <summary>Non-generic convenience wrapper for endpoints returning no data</summary>
public static class ApiResponse
{
    public static ApiResponse<object> Ok(string? message = null) =>
        ApiResponse<object>.Ok(new { }, message);

    public static ApiResponse<object> Fail(string message) =>
        ApiResponse<object>.Fail(message);

    public static ApiResponse<object> Fail(IEnumerable<string> errors) =>
        ApiResponse<object>.Fail(errors);
}
