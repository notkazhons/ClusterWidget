using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ClusterWidget;

/// <summary>
/// Optional timing log. Off unless the CLUSTERWIDGET_PERF environment variable is set, in which case measurements are
/// appended to that file. The checks are a single static bool, so the normal build pays nothing for them.
/// </summary>
internal static class Perf
{
    private static readonly string? Path = Environment.GetEnvironmentVariable("CLUSTERWIDGET_PERF") is { Length: > 0 } p ? p : null;
    public static bool On => Path != null;

    public static long Start() => Stopwatch.GetTimestamp();
    public static double Ms(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;

    public static void Log(string line)
    {
        if (Path == null) return;
        try { File.AppendAllText(Path, line + "\n"); } catch (Exception) { /* measuring must never break the app */ }
    }

    /// <summary>Collects durations and writes "name: n=.. avg=.. p95=.. max=.." when asked.</summary>
    public sealed class Series
    {
        private readonly List<double> _v = new();
        public void Add(double ms) { if (On) _v.Add(ms); }
        public void Flush(string label)
        {
            if (!On || _v.Count == 0) return;
            var s = _v.OrderBy(x => x).ToArray();
            Log($"{label}: n={s.Length} avg={s.Average():F2}ms p95={s[(int)(s.Length * 0.95)]:F2}ms max={s[^1]:F2}ms");
            _v.Clear();
        }
    }
}
