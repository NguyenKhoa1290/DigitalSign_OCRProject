using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ApiGateway.Monitoring;

public static class RequestJournal
{
    private static readonly HashSet<string> SafeSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "api","auth","login","logout","refresh-token","validate-token","forgot-password","reset-password",
        "change-password","send-email-verification","users","roles","departments","tree","stats","assignees","me",
        "documents","types","file","upload","ocr","submit","dept-sign","submit-director","director-sign","reject","publish","assign",
        "signatures","certificates","issue","personal-sign","legal-seal","document","verify","health","process","process-upload"
    };
    public static (string Service,string Path,Guid? Resource)? Describe(string path)
    {
        var service = path.StartsWith("/api/auth/",StringComparison.Ordinal) || path.StartsWith("/api/users",StringComparison.Ordinal)
            || path.StartsWith("/api/roles",StringComparison.Ordinal) || path.StartsWith("/api/departments",StringComparison.Ordinal) ? "IdentityService"
            : path.StartsWith("/api/documents",StringComparison.Ordinal) ? "DocumentService"
            : path.StartsWith("/api/signatures",StringComparison.Ordinal) ? "SignService"
            : path.StartsWith("/api/ocr",StringComparison.Ordinal) ? "OCRService" : null;
        if(service==null) return null;
        var segments=path.Split('/',StringSplitOptions.RemoveEmptyEntries);
        Guid? resource=null;
        foreach(var segment in segments)
        {
            if(Guid.TryParse(segment,out var id)) {resource ??= id;continue;}
            if(!SafeSegments.Contains(segment)) return(service,"/api/[unmapped]",null);
        }
        return(service,"/"+string.Join('/',segments),resource);
    }
    public static string Action(string method,string path)
    {
        var operation=path.Split('/').Last();
        if(path.StartsWith("/api/auth/")) return operation switch
        {
            "login"=>"Login", "logout"=>"Logout", "change-password"=>"ChangePassword",
            "reset-password"=>"ResetPassword", "forgot-password"=>"RequestPasswordReset", _=>"Authentication"
        };
        if(path.StartsWith("/api/signatures/")) return operation switch
        {
            "personal-sign"=>"PersonalSign", "legal-seal"=>"LegalSeal", "issue"=>"IssueCertificate", _=>"RevokeCertificate"
        };
        if(path.StartsWith("/api/ocr/"))return "ProcessOCR";
        if(path.StartsWith("/api/documents/"))
        {
            var workflow=operation switch
            {
                "upload"=>"UploadDocument", "submit"=>"Submit", "dept-sign"=>"DeptSign", "submit-director"=>"SubmitDirector",
                "director-sign"=>"DirectorSign", "reject"=>"Reject", "publish"=>"Publish", "assign"=>"Assign", "ocr"=>"UpdateOCR", _=>null
            };
            if(workflow!=null)return workflow;
        }
        if(path.Contains("/roles/",StringComparison.Ordinal))return method=="DELETE"?"RemoveRole":"AssignRole";
        var entity=path.StartsWith("/api/users")?"User":path.StartsWith("/api/departments")?"Department":"Document";
        return (method switch {"POST"=>"Create","PUT"=>"Update","DELETE"=>"Delete","PATCH"=>"Update",_=>"Read"})+entity;
    }
    public static async Task CaptureAsync(HttpContext context, Func<Task> next)
    {
        var description=Describe(context.Request.Path.Value??"");
        if(description==null) {await next();return;}
        var store=context.RequestServices.GetRequiredService<EventStore>();
        var logger=context.RequestServices.GetRequiredService<ILogger<EventStore>>();
        string? loginUsername=null;
        if(context.Request.Method=="POST" && context.Request.Path=="/api/auth/login" && context.Request.ContentLength is >0 and <=8192)
        {
            context.Request.EnableBuffering();
            try
            {
                using var json=await JsonDocument.ParseAsync(context.Request.Body);
                if(json.RootElement.ValueKind==JsonValueKind.Object)
                    foreach(var property in json.RootElement.EnumerateObject())
                        if(property.Name.Equals("username",StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind==JsonValueKind.String)
                        {
                            var value=property.Value.GetString();
                            if(value!=null && Regex.IsMatch(value,@"\A[a-zA-Z0-9_.-]{3,50}\z")) loginUsername=value;
                        }
            }
            catch(JsonException) { }
            finally {context.Request.Body.Position=0;}
        }
        var start=Stopwatch.GetTimestamp(); var failure=false;
        var requestTrace=Activity.Current?.TraceId.ToString()??context.TraceIdentifier;
        context.Response.Headers["X-Trace-Id"]=requestTrace;
        try {await next();} catch {failure=true;throw;}
        finally
        {
            try
            {
                var (service,path,resource)=description.Value;
                var actorText=context.User.FindFirstValue(ClaimTypes.NameIdentifier)??context.User.FindFirstValue("sub");
                Guid? actor=Guid.TryParse(actorText,out var id)?id:null;
                var status=failure?500:context.Response.StatusCode;
                if(status<400 && loginUsername!=null) actor=await store.FindLoginActorAsync(loginUsername);
                var trace=requestTrace;
                var method=context.Request.Method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" ? context.Request.Method : "OTHER";
                await store.RecordAsync("Request",service,"Request",actor,resource,method,path,status,trace,Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                if(method is "POST" or "PUT" or "PATCH" or "DELETE"
                    && path!="/api/[unmapped]"
                    && !path.EndsWith("validate-token") && !path.EndsWith("refresh-token")
                    && (service!="DocumentService" || status>=400 || method=="DELETE" || path.EndsWith("/upload")))
                    await store.RecordAsync("Audit",service,Action(method,path),actor,resource,method,path,status,trace,Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            catch(Exception ex) when(ex is not OutOfMemoryException)
            {
                // Do not let diagnostics turn a completed business request into an error.
                logger.LogWarning("Không lưu được nhật ký API: {ErrorType}",ex.GetType().Name);
            }
        }
    }
}
