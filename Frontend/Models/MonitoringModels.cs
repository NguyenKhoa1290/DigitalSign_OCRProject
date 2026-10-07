namespace HauDocumentApp.Models;

public class JournalEvent
{
    public Guid Id { get; set; }
    public string Service { get; set; } = "";
    public string Level { get; set; } = "";
    public string Action { get; set; } = "";
    public Guid? ActorId { get; set; }
    public string? ActorName { get; set; }
    public Guid? ResourceId { get; set; }
    public DateTime Timestamp { get; set; }
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public int StatusCode { get; set; }
    public string TraceId { get; set; } = "";
    public double ElapsedMs { get; set; }
}
public class NotificationDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "";
    public string Message { get; set; } = "";
    public string TargetUrl { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public DateTime? ReadAt { get; set; }
}
public class JournalPage<T>
{
    public List<T> Items { get; set; } = new();
    public long TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long UnreadCount { get; set; }
}
