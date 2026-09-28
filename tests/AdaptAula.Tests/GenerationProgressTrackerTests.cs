using AdaptAula.Api.Services;
using Xunit;

namespace AdaptAula.Tests;

public class GenerationProgressTrackerTests
{
    [Fact]
    public void Get_ReturnsNull_ForAPlanThatNeverStarted()
    {
        var tracker = new GenerationProgressTracker();

        Assert.Null(tracker.Get(Guid.NewGuid()));
    }

    [Fact]
    public void Start_ThenIncrement_AccumulatesCurrentWithoutChangingTotal()
    {
        var tracker = new GenerationProgressTracker();
        var planId = Guid.NewGuid();

        tracker.Start(planId, total: 10);
        tracker.Increment(planId, by: 4);
        tracker.Increment(planId, by: 3);

        var progress = tracker.Get(planId);
        Assert.NotNull(progress);
        Assert.Equal(7, progress!.Value.Current);
        Assert.Equal(10, progress.Value.Total);
    }

    [Fact]
    public void Complete_RemovesTheEntry_SoASubsequentGetReturnsNull()
    {
        var tracker = new GenerationProgressTracker();
        var planId = Guid.NewGuid();

        tracker.Start(planId, total: 5);
        tracker.Increment(planId, by: 5);
        tracker.Complete(planId);

        Assert.Null(tracker.Get(planId));
    }

    [Fact]
    public void TracksMultiplePlansIndependently()
    {
        var tracker = new GenerationProgressTracker();
        var planA = Guid.NewGuid();
        var planB = Guid.NewGuid();

        tracker.Start(planA, total: 3);
        tracker.Start(planB, total: 9);
        tracker.Increment(planA, by: 1);

        Assert.Equal((1, 3), tracker.Get(planA));
        Assert.Equal((0, 9), tracker.Get(planB));
    }
}
