namespace GvrLicense.Infrastructure.Storage;

/// <summary>Progreso de una subida Admin → MinIO (consultable por polling).</summary>
public sealed class ReleaseUploadProgressStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Snapshot> _items = new();

    public void Start(Guid id) =>
        _items[id] = new Snapshot(0, "Preparando…", Done: false, Error: null);

    public void Set(Guid id, int percent, string phase) =>
        _items[id] = new Snapshot(Math.Clamp(percent, 0, 99), phase, Done: false, Error: null);

    public void Complete(Guid id) =>
        _items[id] = new Snapshot(100, "Listo", Done: true, Error: null);

    public void Fail(Guid id, string error) =>
        _items[id] = new Snapshot(
            _items.TryGetValue(id, out var current) ? current.Percent : 0,
            "Error",
            Done: true,
            Error: error);

    public Snapshot? Get(Guid id) =>
        _items.TryGetValue(id, out var snapshot) ? snapshot : null;

    public void Remove(Guid id) => _items.TryRemove(id, out _);

    public sealed record Snapshot(int Percent, string Phase, bool Done, string? Error);
}
