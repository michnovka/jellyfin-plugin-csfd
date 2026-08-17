using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Csfd.Csfd;

/// <summary>
/// Persists when each item's ČSFD rating was last fetched, so the scheduled
/// task can skip items refreshed recently. Stored as a small JSON file in the
/// server data directory. Registered as a singleton.
/// </summary>
public sealed class CsfdStateStore
{
    private readonly string _file;
    private readonly ILogger<CsfdStateStore> _logger;
    private readonly Dictionary<Guid, DateTimeOffset> _fetchedAt;
    private readonly Lock _lock = new();
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;
    private bool _dirty;

    public CsfdStateStore(IApplicationPaths applicationPaths, ILogger<CsfdStateStore> logger)
    {
        _logger = logger;
        _file = Path.Combine(applicationPaths.DataPath, "csfd-rating-state.json");
        _fetchedAt = Load();
    }

    public DateTimeOffset? GetFetchedAt(Guid itemId)
    {
        lock (_lock)
        {
            return _fetchedAt.TryGetValue(itemId, out var value) ? value : null;
        }
    }

    public void SetFetchedAt(Guid itemId, DateTimeOffset when)
    {
        lock (_lock)
        {
            _fetchedAt[itemId] = when;
            _dirty = true;

            // Rewriting the whole dictionary per item would be quadratic over a
            // full-library run; batch writes and rely on Flush() at the end.
            if (DateTimeOffset.UtcNow - _lastSave > TimeSpan.FromSeconds(30))
            {
                SaveLocked();
            }
        }
    }

    /// <summary>Persist any pending changes immediately.</summary>
    public void Flush()
    {
        lock (_lock)
        {
            if (_dirty)
            {
                SaveLocked();
            }
        }
    }

    private void SaveLocked()
    {
        try
        {
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_fetchedAt));
            File.Move(tmp, _file, overwrite: true);
            _dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not persist ČSFD rating state");
        }
        finally
        {
            _lastSave = DateTimeOffset.UtcNow;
        }
    }

    private Dictionary<Guid, DateTimeOffset> Load()
    {
        try
        {
            if (File.Exists(_file) && new FileInfo(_file).Length <= 4 * 1024 * 1024)
            {
                return JsonSerializer.Deserialize<Dictionary<Guid, DateTimeOffset>>(File.ReadAllText(_file)) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogDebug(ex, "Could not load ČSFD rating state");
        }

        return [];
    }
}
