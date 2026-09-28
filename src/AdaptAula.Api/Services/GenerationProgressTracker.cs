using System.Collections.Concurrent;

namespace AdaptAula.Api.Services;

/// <summary>Lets the "Generando…" screen poll real progress instead of showing a static label
/// while <see cref="AdaptationPipelineService"/> works through a plan's questions. Deliberately
/// in-memory rather than a DB column: progress is transient (nothing worth surviving a restart or
/// a schema migration), and it's only ever read by the same process that's writing it.</summary>
public class GenerationProgressTracker
{
    private readonly ConcurrentDictionary<Guid, (int Current, int Total)> _progress = new();

    public void Start(Guid planId, int total) => _progress[planId] = (0, total);

    public void Increment(Guid planId, int by = 1) =>
        _progress.AddOrUpdate(planId, (by, by), (_, existing) => (existing.Current + by, existing.Total));

    public (int Current, int Total)? Get(Guid planId) =>
        _progress.TryGetValue(planId, out var progress) ? progress : null;

    /// <summary>Called in a <c>finally</c> once generation ends (success or failure) so a stale
    /// entry never lingers for a plan that isn't actively generating.</summary>
    public void Complete(Guid planId) => _progress.TryRemove(planId, out _);
}
