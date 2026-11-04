namespace HauDocumentApp.Services;

public sealed class UploadProgressService
{
    public sealed class Upload
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name { get; init; } = "";
        public int? Percent { get; set; }
        public string State { get; set; } = "Preparing";
        public string? ErrorMessage { get; set; }
    }
    private readonly List<Upload> _items = new();
    public IReadOnlyList<Upload> Items => _items;
    public event Action? Changed;
    public Upload Start(string name)
    {
        var item = new Upload { Name = name }; _items.Add(item); Changed?.Invoke(); return item;
    }
    public void Report(Guid id, long loaded, long total, bool computable)
    {
        var item = _items.Find(x => x.Id == id);
        if (item == null || item.State is "Processing" or "Success" or "Error") return;
        item.State = "Uploading";
        if (computable && total > 0) item.Percent = Math.Clamp((int)(100.0 * loaded / total), item.Percent ?? 0, 100);
        Changed?.Invoke();
    }
    public void Uploaded(Guid id)
    {
        var item = _items.Find(x => x.Id == id);
        if (item == null || item.State is "Success" or "Error") return;
        item.State = "Processing"; item.Percent = 100; Changed?.Invoke();
    }
    public void Finish(Guid id, bool success, string? errorMessage = null)
    {
        var item = _items.Find(x => x.Id == id); if (item == null) return;
        item.State = success ? "Success" : "Error"; if (success) item.Percent = 100;
        item.ErrorMessage = errorMessage;
        if (!success && item.Percent == null) item.Percent = 0;
        Changed?.Invoke(); if (success) _ = AutoDismiss(id);
    }
    private async Task AutoDismiss(Guid id) { await Task.Delay(5000); Dismiss(id); }
    public void Dismiss(Guid id) { _items.RemoveAll(x => x.Id == id); Changed?.Invoke(); }
}
