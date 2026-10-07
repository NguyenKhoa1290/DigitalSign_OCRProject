using System.Security.Claims;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Monitoring;

[ApiController,Route("api/admin"),Authorize(Roles="Admin")]
public class MonitoringController(EventStore store):ControllerBase
{
    [HttpGet("system-logs")]
    public Task<IActionResult> Logs([FromQuery] EventQuery query)=>Read("Request",query);
    [HttpGet("activity")]
    public Task<IActionResult> Activity([FromQuery] EventQuery query)=>Read("Audit",query);
    private async Task<IActionResult> Read(string kind,EventQuery query)
    {
        if(query.From>query.To || query.TraceId?.Length>100
            || (!string.IsNullOrEmpty(query.Service) && query.Service is not ("IdentityService" or "DocumentService" or "SignService" or "OCRService" or "ApiGateway"))
            || (!string.IsNullOrEmpty(query.Level) && query.Level is not ("Information" or "Warning" or "Error")))
            return BadRequest(new {success=false,message="Bộ lọc không hợp lệ."});
        Response.Headers.CacheControl="no-store";
        try {return Ok(new {success=true,data=await store.GetEventsAsync(kind,query,HttpContext.RequestAborted)});}
        catch(Exception ex) when(ex is Npgsql.NpgsqlException or TimeoutException)
        {return StatusCode(503,new {success=false,message="Chưa tải được nhật ký. Vui lòng thử lại."});}
    }
}

[ApiController,Route("api/notifications"),Authorize]
public class NotificationsController(EventStore store,IHttpClientFactory factory,ILogger<NotificationsController> logger):ControllerBase
{
    private Guid? CurrentUser=>Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier)??User.FindFirstValue("sub"),out var id)?id:null;
    [HttpGet]
    public async Task<IActionResult> List([FromQuery]int page=1,[FromQuery]bool unreadOnly=false,[FromQuery]string? kind=null)
    {
        if(CurrentUser is not Guid user)return Unauthorized();
        if(!string.IsNullOrEmpty(kind) && kind is not ("Assignment" or "OcrCompleted" or "ReviewRequested" or "Rejected" or "Signed" or "Published" or "CertificateExpiring"))
            return BadRequest(new {success=false,message="Loại thông báo không hợp lệ."});
        Response.Headers.CacheControl="no-store";
        try
        {
            await CheckCertificateAsync(user);
            return Ok(new {success=true,data=await store.GetNotificationsAsync(user,page,unreadOnly,kind,HttpContext.RequestAborted)});
        }
        catch(Exception ex) when(ex is Npgsql.NpgsqlException or TimeoutException)
        {return StatusCode(503,new {success=false,message="Chưa tải được thông báo. Vui lòng thử lại."});}
    }
    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)=>await Read(id);
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAll()=>await Read(null);
    private async Task<IActionResult> Read(Guid? id)
    {
        if(CurrentUser is not Guid user)return Unauthorized();
        Response.Headers.CacheControl="no-store";
        try
        {
            return await store.MarkReadAsync(user,id,HttpContext.RequestAborted)?Ok(new {success=true,data=true}):NotFound(new {success=false,message="Không tìm thấy thông báo."});
        }
        catch(Exception ex) when(ex is Npgsql.NpgsqlException or TimeoutException)
        {return StatusCode(503,new {success=false,message="Không cập nhật được trạng thái thông báo."});}
    }
    private async Task CheckCertificateAsync(Guid user)
    {
        if(!User.IsInRole("Manager") && !User.IsInRole("BoardOfDirectors") && !User.IsInRole("Admin"))return;
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,$"api/signatures/certificates/{user}");
            request.Headers.TryAddWithoutValidation("Authorization",Request.Headers.Authorization.ToString());
            using var response=await factory.CreateClient("notification-certificates").SendAsync(request,HttpContext.RequestAborted);
            if(response.StatusCode==HttpStatusCode.NotFound)return;
            if(!response.IsSuccessStatusCode)return;
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if(json.RootElement.ValueKind!=JsonValueKind.Object || !json.RootElement.TryGetProperty("data",out var data) || data.ValueKind!=JsonValueKind.Object)return;
            if(data.TryGetProperty("notAfter",out var end) && end.ValueKind==JsonValueKind.String && end.TryGetDateTime(out var expiry)
                && data.TryGetProperty("notBefore",out var begin) && begin.ValueKind==JsonValueKind.String && begin.TryGetDateTime(out var start) && start<=DateTime.UtcNow
                && data.TryGetProperty("thumbprint",out var thumbprint) && thumbprint.ValueKind==JsonValueKind.String
                && !string.IsNullOrWhiteSpace(thumbprint.GetString())
                && expiry>DateTime.UtcNow && expiry<=DateTime.UtcNow.AddDays(30))
                await store.AddCertificateExpiryAsync(user,thumbprint.GetString()!,User.IsInRole("Admin")?"/admin/certificates":"/certificates/me",HttpContext.RequestAborted);
        }
        catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or JsonException)
        {logger.LogWarning("Không kiểm tra được hạn chứng thư: {ErrorType}",ex.GetType().Name);}
    }
}
