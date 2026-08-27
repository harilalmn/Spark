using System;
using Spark.Api;
using Spark.Geometry;

namespace Spark.Engine;

/// <summary>
/// Everything one evaluation run needs that is not the graph itself: the document tolerance, the
/// run counter, where work runs, and where results are kept.
/// </summary>
/// <remarks>
/// The tolerance lives here rather than in a static because it is hashed into every cache key.
/// An ambient tolerance would be invisible to the key, so changing it would invalidate nothing and
/// the graph would go on serving geometry computed at the old value, silently. Threading it through
/// the context is what makes "changing document tolerance invalidates exactly the affected nodes"
/// true rather than aspirational.
/// </remarks>
public sealed class EvaluationContext
{
    /// <summary>Creates a context.</summary>
    /// <param name="tolerance">The document tolerance. Hashed into every cache key.</param>
    /// <param name="scheduler">Where node work runs. Defaults to sequential.</param>
    /// <param name="cache">Where results are kept. Defaults to a fresh cache.</param>
    /// <param name="runEpoch">
    /// The run counter, mixed into the keys of impure nodes. Increment it once per run.
    /// </param>
    public EvaluationContext(
        Tolerance tolerance = default,
        IEvaluationScheduler? scheduler = null,
        EvaluationCache? cache = null,
        long runEpoch = 0)
    {
        Tolerance = tolerance;
        Scheduler = scheduler ?? new SequentialEvaluationScheduler();
        Cache = cache ?? new EvaluationCache();
        RunEpoch = runEpoch;
    }

    /// <summary>The document tolerance.</summary>
    public Tolerance Tolerance { get; }

    /// <summary>Where node work runs.</summary>
    public IEvaluationScheduler Scheduler { get; }

    /// <summary>Where results are kept between runs.</summary>
    public EvaluationCache Cache { get; }

    /// <summary>The run counter, mixed into the cache keys of impure nodes.</summary>
    public long RunEpoch { get; }

    /// <summary>Returns a copy for the next run, with the run epoch advanced.</summary>
    /// <returns>A new context sharing this one's cache and scheduler.</returns>
    public EvaluationContext NextRun() => new(Tolerance, Scheduler, Cache, RunEpoch + 1);

    /// <summary>Returns a copy with a different document tolerance, sharing the cache.</summary>
    /// <param name="tolerance">The new tolerance.</param>
    /// <returns>A new context.</returns>
    public EvaluationContext WithTolerance(Tolerance tolerance) => new(tolerance, Scheduler, Cache, RunEpoch);
}
