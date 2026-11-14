using System.Text.Json;

namespace Workbench.App;

/// <summary>One root's selected preference. A failed chosen write remains dirty until a joined retry.
/// An untouched corrupt-load fallback is not a choice and never writes on departure.</summary>
internal sealed class HostThemeSession(string root)
{
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly object _gate = new();
    private int _selected, _saved;
    private bool _dirty, _repair;
    private byte[]? _damaged;
    private string? _backup;
    private string? _error;
    internal string UserRoot { get; } = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    internal int SelectedTheme { get { lock (_gate) return _selected; } }
    internal bool IsDirty { get { lock (_gate) return _dirty; } }
    internal bool NeedsRepair { get { lock (_gate) return _repair; } }
    internal string? ErrorCode { get { lock (_gate) return _error; } }
    private string PathName => Path.Combine(UserRoot, "host-settings.json");

    internal void Load()
    {
        var loaded = HostFailure.ReadTheme(() =>
        {
            HostRuntime.RejectLinks(PathName);
            if (!File.Exists(PathName)) return 0;
            var bytes = File.ReadAllBytes(PathName);
            _damaged = bytes;
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Count() != 1
                || !document.RootElement.TryGetProperty("Theme", out var value)
                || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var theme) || theme is < 0 or > 2) throw new JsonException();
            _damaged = null;
            return theme;
        }, 0);
        lock (_gate)
        {
            _selected = _saved = loaded.Theme;
            _dirty = false; _repair = loaded.Error is not null; _error = loaded.Error;
        }
    }

    internal void Select(int theme)
    {
        if (theme is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(theme));
        lock (_gate) { _selected = theme; _dirty = theme != _saved || _repair || _dirty; }
    }

    /// <summary>Only the explicit conditional recovery action may acknowledge newly observed bytes.
    /// Keep the selected choice and prior backup files; ordinary Flush never rebases its snapshot.</summary>
    internal async Task ObserveRecoveryAsync()
    {
        await _writes.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate) if (!_repair) throw new InvalidOperationException("Theme recovery is not required.");
            HostRuntime.RejectLinks(PathName);
            var original = await File.ReadAllBytesAsync(PathName).ConfigureAwait(false);
            if (_damaged is null || !original.SequenceEqual(_damaged))
            {
                _damaged = original;
                _backup = null; // Prior immutable backup remains on disk; these bytes need their own.
            }
            lock (_gate) _dirty = true;
        }
        catch (Exception error)
        {
            lock (_gate) _error = HostFailure.Describe("ThemeSettingsFailed", error);
            throw;
        }
        finally { _writes.Release(); }
    }

    internal async Task FlushAsync()
    {
        await _writes.WaitAsync().ConfigureAwait(false);
        try
        {
            int selected; bool repair;
            lock (_gate) { if (!_dirty) return; selected = _selected; repair = _repair; }
            try
            {
                HostRuntime.RejectLinks(PathName);
                Directory.CreateDirectory(UserRoot);
                if (repair)
                {
                    // A retained backup acknowledges only its observed original, never later bytes.
                    if (!File.Exists(PathName)) throw new IOException();
                    var original = await File.ReadAllBytesAsync(PathName).ConfigureAwait(false);
                    if (_damaged is not null && !original.SequenceEqual(_damaged)) throw new IOException();
                    if (_backup is null)
                    {
                        var backup = PathName + $".corrupt-{Guid.NewGuid():N}.bak";
                        HostRuntime.RejectLinks(backup);
                        await using (var output = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            await output.WriteAsync(original).ConfigureAwait(false);
                        _backup = backup;
                    }
                }
                var temporary = PathName + ".tmp";
                HostRuntime.RejectLinks(temporary);
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Preferences(selected))).ConfigureAwait(false);
                // Validate again immediately before replacement, including retries with a backup.
                HostRuntime.RejectLinks(PathName);
                if (repair && !File.ReadAllBytes(PathName).SequenceEqual(_damaged ?? [])) throw new IOException();
                File.Move(temporary, PathName, overwrite: true);
                lock (_gate) { _saved = selected; _dirty = _selected != selected; _repair = false; _error = null; _damaged = null; }
            }
            catch (Exception error)
            {
                lock (_gate) { _dirty = true; _error = HostFailure.Describe("ThemeSettingsFailed", error); }
                throw;
            }
        }
        finally { _writes.Release(); }
    }

    private sealed record Preferences(int Theme);
}
