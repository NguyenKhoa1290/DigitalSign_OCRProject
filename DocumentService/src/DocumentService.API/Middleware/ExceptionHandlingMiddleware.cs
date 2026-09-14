using DocumentService.Core.Exceptions;
using System.Net;
using System.Text.Json;

namespace DocumentService.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next   = next;
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
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message) = exception switch
        {
            DocumentNotFoundException           => (HttpStatusCode.NotFound,           exception.Message),
            InvalidWorkflowTransitionException  => (HttpStatusCode.UnprocessableEntity, exception.Message),
            UnauthorizedDocumentAccessException => (HttpStatusCode.Forbidden,           exception.Message),
            DocumentServiceException            => (HttpStatusCode.BadRequest,          exception.Message),
            UnauthorizedAccessException         => (HttpStatusCode.Forbidden,           "Bạn không có quyền thực hiện thao tác này."),
            _                                   => (HttpStatusCode.InternalServerError, "Đã xảy ra lỗi hệ thống, vui lòng thử lại sau.")
        };

        context.Response.StatusCode = (int)statusCode;

        var response = new
        {
            success = false,
            message,
            data    = (object?)null,
            errors  = new List<string> { message }
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app)
        => app.UseMiddleware<ExceptionHandlingMiddleware>();
}
