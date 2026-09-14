using IdentityService.Core.Exceptions;
using System.Net;
using System.Text.Json;

namespace IdentityService.API.Middleware;

/// <summary>
/// Global exception handler middleware - bắt tất cả exception và trả về response chuẩn
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message) = exception switch
        {
            UserNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            RoleNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            DepartmentNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            InvalidCredentialsException => (HttpStatusCode.Unauthorized, exception.Message),
            AccountLockedException => (HttpStatusCode.Forbidden, exception.Message),
            TokenExpiredException => (HttpStatusCode.Unauthorized, exception.Message),
            UserAlreadyExistsException => (HttpStatusCode.Conflict, exception.Message),
            IdentityServiceException => (HttpStatusCode.BadRequest, exception.Message),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Bạn không có quyền thực hiện thao tác này"),
            _ => (HttpStatusCode.InternalServerError, "Đã xảy ra lỗi hệ thống, vui lòng thử lại sau")
        };

        context.Response.StatusCode = (int)statusCode;

        var response = new
        {
            success = false,
            message = message,
            data = (object?)null,
            errors = new List<string> { message }
        };

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, jsonOptions));
    }
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app)
        => app.UseMiddleware<ExceptionHandlingMiddleware>();
}
