using System.Diagnostics;
using System.Globalization;

namespace DeskNote.Benchmarks;

/// <summary>One measured operation against the target the report sets for it.</summary>
/// <param name="Name">What was measured.</param>
/// <param name="Samples">Every timing, in milliseconds.</param>
/// <param name="TargetMs">The release target, or null when the number is informational.</param>
/// <param name="Source">Where the target comes from, so a failing line is traceable.</param>
public sealed record Measurement(string Name, IReadOnlyList<double> Samples, double? TargetMs, string Source)
{
    public double P50 => Percentile(50);

    public double P95 => Percentile(95);

    public double Max => Samples.Count == 0 ? 0 : Samples.Max();

    /// <summary>
    /// Whether the measurement meets its target.
    /// </summary>
    /// <remarks>
    /// Judged on p95, which is what the report states. A mean would hide exactly the stalls a user
    /// notices, and a max would fail the run on one unlucky scheduling hiccup.
    /// </remarks>
    public bool Passes => TargetMs is null || P95 <= TargetMs;

    /// <summary>
    /// The percentile using nearest-rank on the sorted samples.
    /// </summary>
    /// <remarks>
    /// Nearest-rank rather than interpolation: with a few hundred samples the difference is noise,
    /// and reporting a value that was actually observed is easier to argue about than one computed
    /// between two neighbours.
    /// </remarks>
    private double Percentile(int percentile)
    {
        if (Samples.Count == 0)
        {
            return 0;
        }

        var sorted = Samples.Order().ToArray();
        var rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }

    public string ToRow()
    {
        var target = TargetMs is null
            ? "—"
            : "< " + TargetMs.Value.ToString("0.#", CultureInfo.InvariantCulture) + " ms";

        var verdict = TargetMs is null ? "info" : Passes ? "PASS" : "FAIL";

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0,-34} {1,9:0.00} {2,9:0.00} {3,9:0.00} {4,12} {5,-6} {6}",
            Name, P50, P95, Max, target, verdict, Source);
    }

    public static string Header() => string.Format(
        CultureInfo.InvariantCulture,
        "{0,-34} {1,9} {2,9} {3,9} {4,12} {5,-6} {6}",
        "operation", "p50 ms", "p95 ms", "max ms", "target", "", "source");
}

/// <summary>Runs an operation repeatedly and collects timings.</summary>
public static class Benchmark
{
    /// <summary>
    /// Times <paramref name="action"/> <paramref name="iterations"/> times after warming up.
    /// </summary>
    /// <remarks>
    /// The warm-up is not optional here: the first call through any of these paths pays for JIT,
    /// the SQLite page cache and Dapper's first-time reflection. Including that in the samples
    /// would measure startup once and call it a p95.
    /// </remarks>
    public static async Task<Measurement> MeasureAsync(
        string name,
        int iterations,
        Func<int, Task> action,
        double? targetMs,
        string source,
        int warmup = 20)
    {
        for (var i = 0; i < warmup; i++)
        {
            await action(i).ConfigureAwait(false);
        }

        var samples = new List<double>(iterations);
        var stopwatch = new Stopwatch();

        for (var i = 0; i < iterations; i++)
        {
            stopwatch.Restart();
            await action(i).ConfigureAwait(false);
            stopwatch.Stop();
            samples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        return new Measurement(name, samples, targetMs, source);
    }
}
