using System.Threading;

namespace CreoToolkit.Agent.Handles;

internal sealed class HandleRegistry : IDisposable
{
    private const int TokenVersion = 1;

    private readonly Dictionary<long, RegistryEntry> _entries = new();
    private readonly object _lock = new();
    private readonly Guid _registryId = Guid.NewGuid();
    private readonly int _sessionEpoch;
    private int _disposed;
    private long _nextId;

    public HandleRegistry(int sessionEpoch)
    {
        _sessionEpoch = sessionEpoch;
    }

    internal int Count
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    public HandleId Track<T>(T resource, string kind, long? ttlExpiresAtUnixMs = null)
        where T : class, IDisposable
    {
        ThrowUtil.IfNull(resource);
        ThrowUtil.IfNullOrWhiteSpace(kind);

        lock (_lock)
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(HandleRegistry));

            var id = ++_nextId;
            _entries[id] = new RegistryEntry(kind, resource, ttlExpiresAtUnixMs);
            return new HandleId(
                id,
                kind,
                _sessionEpoch,
                _registryId,
                TokenVersion,
                Mac: null,
                ttlExpiresAtUnixMs);
        }
    }

    public T? Resolve<T>(HandleId token, string expectedKind)
        where T : class, IDisposable
    {
        ThrowUtil.IfNullOrWhiteSpace(expectedKind);

        IDisposable? toDispose = null;
        T? result = null;

        lock (_lock)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return null;

            if (!TryValidateTokenCore(token, expectedKind, out var entry))
                return null;

            if (IsExpired(entry.TtlExpiresAtUnixMs))
            {
                _entries.Remove(token.Id);
                toDispose = entry.Resource;
            }
            else
            {
                result = entry.Resource as T;
            }
        }

        toDispose?.Dispose();
        return result;
    }

    public bool Release(HandleId token, string expectedKind)
    {
        ThrowUtil.IfNullOrWhiteSpace(expectedKind);

        IDisposable? toDispose = null;
        var released = false;

        lock (_lock)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return false;

            if (!TryValidateTokenCore(token, expectedKind, out var entry))
                return false;

            _entries.Remove(token.Id);
            toDispose = entry.Resource;
            released = !IsExpired(entry.TtlExpiresAtUnixMs);
        }

        toDispose?.Dispose();
        return released;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        List<IDisposable> pending;
        lock (_lock)
        {
            pending = _entries.Values.Select(static entry => entry.Resource).ToList();
            _entries.Clear();
        }

        foreach (var resource in pending)
            resource.Dispose();
    }

    private bool TryValidateTokenCore(HandleId token, string expectedKind, out RegistryEntry entry)
    {
        entry = default;

        if (token.Mac is not null)
            return false;

        if (token.RegistryId != _registryId)
            return false;

        if (token.SessionEpoch != _sessionEpoch)
            return false;

        if (token.Version != TokenVersion)
            return false;

        if (!_entries.TryGetValue(token.Id, out entry))
            return false;

        if (!string.Equals(token.Kind, expectedKind, StringComparison.Ordinal))
            return false;

        if (!string.Equals(entry.Kind, expectedKind, StringComparison.Ordinal))
            return false;

        return true;
    }

    private static bool IsExpired(long? ttlExpiresAtUnixMs)
    {
        return ttlExpiresAtUnixMs is long ttl
            && ttl < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}

internal readonly record struct RegistryEntry(string Kind, IDisposable Resource, long? TtlExpiresAtUnixMs);
