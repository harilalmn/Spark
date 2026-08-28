using Spark.UI.Controls;

namespace Spark.UI.Tests;

/// <summary>
/// The frame-time ring, and the sample size a benchmark quotes from it.
/// </summary>
/// <remarks>
/// Written for a defect rather than for coverage: the canvas benchmark printed a frame count it
/// had not measured over, because the canvas's readout window is 120 frames and the run is 500.
/// See [N31](../../docs/NOTES.md).
/// </remarks>
public sealed class FrameTimerTests
{
    /// <summary>
    /// The default window is smaller than a benchmark run, which is why <c>Resize</c> exists.
    /// </summary>
    /// <remarks>
    /// This is the defect stated as an assertion. If the default ever grows to cover a run, the
    /// benchmark's call to <c>Resize</c> stops being load-bearing and this test says so.
    /// </remarks>
    [Fact]
    public void TheDefaultWindowIsTooSmallForABenchmarkRunAndSilentlyKeepsTheTail()
    {
        FrameTimer timer = new();

        for (int frame = 0; frame < 250; frame++)
        {
            timer.Record(9.0);
        }

        for (int frame = 0; frame < 250; frame++)
        {
            timer.Record(1.0);
        }

        // 500 frames went in; the window kept the last 120, all of which were the cheap ones.
        Assert.Equal(120, timer.Count);
        Assert.Equal(1.0, timer.Mean(), 3);
    }

    /// <summary>The fix: a window sized to the run reports the run.</summary>
    [Fact]
    public void AResizedWindowCoversEveryFrameOfTheRun()
    {
        FrameTimer timer = new();
        timer.Resize(500);

        for (int frame = 0; frame < 250; frame++)
        {
            timer.Record(9.0);
        }

        for (int frame = 0; frame < 250; frame++)
        {
            timer.Record(1.0);
        }

        Assert.Equal(500, timer.Count);
        Assert.Equal(5.0, timer.Mean(), 3);
    }

    /// <summary>A resize empties the window, so a run never inherits the previous one's frames.</summary>
    [Fact]
    public void AResizeEmptiesTheWindow()
    {
        FrameTimer timer = new();
        timer.Record(42.0);

        timer.Resize(500);

        Assert.Equal(0, timer.Count);
        Assert.Equal(0, timer.Mean());
    }

    /// <summary>The floor applies to a resize as it does to construction.</summary>
    [Fact]
    public void AResizeIsClampedToTheSameFloorAsTheConstructor()
    {
        FrameTimer timer = new();
        timer.Resize(1);

        for (int frame = 0; frame < 8; frame++)
        {
            timer.Record(1.0);
        }

        Assert.Equal(8, timer.Count);
    }
}
