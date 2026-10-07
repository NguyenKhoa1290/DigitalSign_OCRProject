using HauDocumentApp.Models;

namespace HauDocumentApp.Services;

public class MonitoringService(ApiService api)
{
    public Task<JournalPage<JournalEvent>?> GetEventsAsync(bool audit,int page,string service,string level,DateTime? from,DateTime? to,string trace,string actor)
    {
        var route=$"api/admin/{(audit?"activity":"system-logs")}?page={page}";
        if(service!="")route+="&service="+Uri.EscapeDataString(service);
        if(level!="")route+="&level="+Uri.EscapeDataString(level);
        if(trace!="")route+="&traceId="+Uri.EscapeDataString(trace.Trim());
        if(actor!="")route+="&actorId="+Uri.EscapeDataString(actor.Trim());
        if(from.HasValue)route+="&from="+Uri.EscapeDataString(DateTime.SpecifyKind(from.Value.Date.AddHours(-7),DateTimeKind.Utc).ToString("O"));
        if(to.HasValue)route+="&to="+Uri.EscapeDataString(DateTime.SpecifyKind(to.Value.Date.AddDays(1).AddHours(-7),DateTimeKind.Utc).ToString("O"));
        return api.GetAsync<JournalPage<JournalEvent>>(route);
    }
    public Task<JournalPage<NotificationDto>?> GetNotificationsAsync(int page,bool unread,string kind,CancellationToken cancellationToken=default)
        =>api.GetAsync<JournalPage<NotificationDto>>($"api/notifications?page={page}&unreadOnly={unread.ToString().ToLowerInvariant()}&kind={Uri.EscapeDataString(kind)}",cancellationToken);
    public Task<bool> MarkReadAsync(Guid id)=>api.PatchAsync<bool>($"api/notifications/{id}/read",new {});
    public Task<bool> MarkAllReadAsync()=>api.PostAsync<bool>("api/notifications/read-all");
}
