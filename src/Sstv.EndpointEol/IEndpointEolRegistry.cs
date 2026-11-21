using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using System.Runtime.CompilerServices;

namespace Sstv.EndpointEol;

/// <summary>
/// Provides snapshot of all endpoints marked with <see cref="EndOfLifeAttribute"/>.
/// </summary>
public interface IEndpointEolRegistry
{
    /// <summary>
    /// Returns all tracked endpoints sorted by block date ascending.
    /// Endpoint list is cached and rebuilt when routing endpoints change, while the current phase
    /// and the number of days left are always calculated against the current time.
    /// </summary>
    /// <returns>Tracked endpoints.</returns>
    IReadOnlyList<EndpointEolInfo> GetSnapshot();
}

/// <inheritdoc />
internal sealed class EndpointEolRegistry : IEndpointEolRegistry
{
    private readonly IEnumerable<EndpointDataSource> _dataSources;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _buildLock = new();
    private volatile CacheEntry? _cache;

    /// <summary>
    /// Creates new instance of <see cref="EndpointEolRegistry"/>.
    /// </summary>
    /// <param name="dataSources">Routing endpoint data sources.</param>
    /// <param name="timeProvider">Time provider.</param>
    public EndpointEolRegistry(IEnumerable<EndpointDataSource> dataSources, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(dataSources);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _dataSources = dataSources;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public IReadOnlyList<EndpointEolInfo> GetSnapshot()
    {
        var cache = _cache;
        if (cache is null || cache.ChangeToken.HasChanged)
        {
            lock (_buildLock)
            {
                cache = _cache;
                if (cache is null || cache.ChangeToken.HasChanged)
                {
                    cache = Build();
                    _cache = cache;
                }
            }
        }

        return Project(cache.Entries);
    }

    /// <summary>
    /// Fills volatile fields (phase, days to block) against the current time,
    /// so a long-lived cache never serves stale dates.
    /// </summary>
    private EndpointEolInfo[] Project(IReadOnlyList<EndpointEolEntry> entries)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var result = new EndpointEolInfo[entries.Count];

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            result[i] = new EndpointEolInfo
            {
                Method = entry.Method,
                Path = entry.Path,
                DisplayName = entry.DisplayName,
                WarnAfter = entry.WarnAfter,
                BlockAfter = entry.BlockAfter,
                ReplacedBy = entry.ReplacedBy,
                Phase = entry.GetPhase(today),
                DaysToBlock = entry.BlockAfter.DayNumber - today.DayNumber
            };
        }

        return result;
    }

    private CacheEntry Build()
    {
        var visited = new HashSet<Endpoint>(EndpointReferenceComparer.Instance);
        var entries = new List<EndpointEolEntry>();

        foreach (var dataSource in _dataSources)
        {
            foreach (var endpoint in dataSource.Endpoints)
            {
                if (!visited.Add(endpoint))
                {
                    continue;
                }

                var attributes = endpoint.Metadata.GetOrderedMetadata<EndOfLifeAttribute>();
                if (attributes.Count == 0)
                {
                    continue;
                }

                // Action-level attribute overrides controller-level one.
                var policy = EndOfLifePolicy.From(attributes[^1], endpoint.DisplayName);
                var httpMethods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
                var method = httpMethods is { Count: > 0 } ? string.Join(", ", httpMethods) : "ANY";

                entries.Add(new EndpointEolEntry(
                    method,
                    RouteTemplate.Normalize((endpoint as RouteEndpoint)?.RoutePattern.RawText),
                    endpoint.DisplayName,
                    policy
                ));
            }
        }

        entries.Sort(CompareByBlockDate);

        var changeTokens = new List<IChangeToken>();
        foreach (var dataSource in _dataSources)
        {
            changeTokens.Add(dataSource.GetChangeToken());
        }

        return new CacheEntry(entries.AsReadOnly(), new CompositeChangeToken(changeTokens));
    }

    private static int CompareByBlockDate(EndpointEolEntry x, EndpointEolEntry y)
    {
        var result = x.BlockAfter.CompareTo(y.BlockAfter);
        if (result != 0)
        {
            return result;
        }

        result = (x.WarnAfter?.DayNumber ?? long.MaxValue).CompareTo(y.WarnAfter?.DayNumber ?? long.MaxValue);
        if (result != 0)
        {
            return result;
        }

        return string.CompareOrdinal(x.Path, y.Path);
    }

    /// <summary>
    /// Immutable description of a tracked endpoint. Phase and days to block are derived on read,
    /// never stored, because they change with wall clock time.
    /// </summary>
    private sealed record EndpointEolEntry(
        string Method,
        string Path,
        string? DisplayName,
        EndOfLifePolicy Policy
    )
    {
        public DateOnly BlockAfter => Policy.BlockAfter;

        public DateOnly? WarnAfter => Policy.WarnAfter;

        public string? ReplacedBy => Policy.ReplacedBy;

        public EndOfLifePhase GetPhase(DateOnly today) => Policy.GetPhase(today);
    }

    private sealed record CacheEntry(IReadOnlyList<EndpointEolEntry> Entries, IChangeToken ChangeToken);

    private sealed class EndpointReferenceComparer : IEqualityComparer<Endpoint>
    {
        public static readonly EndpointReferenceComparer Instance = new();

        private EndpointReferenceComparer()
        {
        }

        public bool Equals(Endpoint? x, Endpoint? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(Endpoint obj)
        {
            ArgumentNullException.ThrowIfNull(obj);

            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}