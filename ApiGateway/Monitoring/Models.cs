namespace ApiGateway.Monitoring;

public class EventQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Service { get; set; }
    public string? Level { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? TraceId { get; set; }
    public Guid? ActorId { get; set; }
}
public record MonitoringEvent(Guid Id, string Service, string Level, string Action, Guid? ActorId,
    string? ActorName, Guid? ResourceId, DateTime Timestamp, string Method, string Path, int StatusCode, string TraceId, double ElapsedMs);
public record UserNotification(Guid Id, string Kind, string Message, string TargetUrl, DateTime Timestamp, DateTime? ReadAt);
public record PageResult<T>(IReadOnlyList<T> Items, long TotalCount, int Page, int PageSize, long UnreadCount = 0);
