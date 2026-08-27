using System;
using System.Globalization;

namespace Spark.UI.Controls;

/// <summary>
/// A fixed-size ring of recent frame durations, and the three numbers worth quoting from them.
/// </summary>
/// <remarks>
/// <para>
/// The mean is reported alongside the median and the 95th percentile because a canvas that
/// averages 8 ms and spikes to 40 ms four times a second feels worse than one that sits flat at
/// 14 ms, and a mean on its own hides exactly that. ADR-0013's 60 fps target is a claim about the
/// 95th percentile, not about the average.
/// </para>
/// <para>
/// Recording is allocation-free and costs one <c>Stopwatch</c> timestamp pair per frame, so this
/// stays on in release builds. A frame counter you have to rebuild to turn on is a frame counter
/// nobody looks at.
/// </para>
/// </remarks>
public sealed class FrameTimer
{
    private readonly double[] _samples;
    private readonly double[] _sorted;
    private int _next;
    private int _filled;

    /// <summary>Creates a timer over a fixed window.</summary>
    /// <param name="capacity">How many frames to keep. Clamped to at least eight.</param>
    public FrameTimer(int capacity = 120)
    {
        capacity = Math.Max(8, capacity);
        _samples = new double[capacity];
        _sorted = new double[capacity];
    }

    /// <summary>How many samples the window currently holds.</summary>
    public int Count => _filled;

    /// <summary>The most recent frame duration in milliseconds.</summary>
    public double LastMilliseconds { get; private set; }

    /// <summary>Records one frame.</summary>
    /// <param name="milliseconds">The duration in milliseconds.</param>
    public void Record(double milliseconds)
    {
        LastMilliseconds = milliseconds;
        _samples[_next] = milliseconds;
        _next = (_next + 1) % _samples.Length;
        if (_filled < _samples.Length)
        {
            _filled++;
        }
    }

    /// <summary>Empties the window, which is what a benchmark does after its warm-up.</summary>
    public void Reset()
    {
        _next = 0;
        _filled = 0;
        LastMilliseconds = 0;
    }

    /// <summary>The arithmetic mean of the window, in milliseconds.</summary>
    /// <returns>Zero when no frames have been recorded.</returns>
    public double Mean()
    {
        if (_filled == 0)
        {
            return 0;
        }

        double total = 0;
        for (int i = 0; i < _filled; i++)
        {
            total += _samples[i];
        }

        return total / _filled;
    }

    /// <summary>A percentile of the window, in milliseconds.</summary>
    /// <param name="fraction">The percentile as a fraction, for example 0.95. Clamped to 0..1.</param>
    /// <returns>Zero when no frames have been recorded.</returns>
    public double Percentile(double fraction)
    {
        if (_filled == 0)
        {
            return 0;
        }

        Array.Copy(_samples, _sorted, _filled);
        Array.Sort(_sorted, 0, _filled);

        double clamped = Math.Clamp(fraction, 0, 1);
        int index = (int)Math.Round(clamped * (_filled - 1), MidpointRounding.AwayFromZero);
        return _sorted[Math.Clamp(index, 0, _filled - 1)];
    }

    /// <summary>A one-line summary for the on-screen readout and for the benchmark's output.</summary>
    /// <returns>Median, 95th percentile and implied frames per second.</returns>
    public string Summary()
    {
        double median = Percentile(0.5);
        double p95 = Percentile(0.95);
        double fps = median > 0 ? 1000.0 / median : 0;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{median:F2} ms median, {p95:F2} ms p95, {fps:F0} fps");
    }
}
