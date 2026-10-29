namespace HWSuite.Tests;

/// <summary>
/// Tests that load every core (benchmarks, stress test) or measure time (network engine, load test) run in this
/// collection: one after another and after the parallel tests, so they neither starve nor slow each other down.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing";
}
