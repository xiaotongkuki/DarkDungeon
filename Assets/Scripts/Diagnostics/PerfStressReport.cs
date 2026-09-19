using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One measured window: what was running, and what it cost.
///
/// Kept separate from the harness so the numbers and their formatting can be read and
/// reviewed as a unit, and so the harness file stays about orchestration.
/// </summary>
public struct PerfStressSample
{
    /// <summary>Enemy count the sample was meant to hold.</summary>
    public int Tier;

    /// <summary>Variant measured.</summary>
    public PerfStressHarness.StressVariant Variant;

    /// <summary>Frames inside the measured window.</summary>
    public int Frames;

    /// <summary>
    /// Real seconds the window lasted, measured on the wall clock.
    ///
    /// Carried separately from <see cref="Frames"/> because the two disagree in the
    /// editor: <see cref="AverageMs"/> is the mean of the player loop's own frame
    /// delta, while this is how long the window really took. Dividing one by the
    /// other is the only honest frame rate, and the gap between them is editor
    /// overhead outside the player loop.
    /// </summary>
    public float WindowSeconds;

    /// <summary>Mean player-loop frame time over the window, in milliseconds.</summary>
    public float AverageMs;

    /// <summary>Fastest frame in the window, in milliseconds.</summary>
    public float MinMs;

    /// <summary>Slowest frame in the window, in milliseconds.</summary>
    public float MaxMs;

    /// <summary>Frame-time standard deviation, in milliseconds; the fluctuation figure.</summary>
    public float StdDevMs;

    /// <summary>Frames per second actually achieved, from frames over window seconds.</summary>
    public float Fps;

    /// <summary>Managed bytes allocated per frame.</summary>
    public float GcBytesPerFrame;

    /// <summary>
    /// Change in total allocated memory over the window, in megabytes.
    ///
    /// A steady load should leave this near zero; a value that grows with the window
    /// is the signature of a leak, and reporting it turns that from a guess into a
    /// measurement.
    /// </summary>
    public float MemoryDeltaMb;

    /// <summary>Mean sprite batches over the window, or -1 outside the editor.</summary>
    public int Batches;

    /// <summary>Mean SetPass calls over the window, or -1 outside the editor.</summary>
    public int SetPassCalls;

    /// <summary>Mean draw calls over the window, or -1 outside the editor.</summary>
    public int DrawCalls;

    /// <summary>Live enemies when the window opened.</summary>
    public int ActiveStart;

    /// <summary>Live enemies when the window closed; below the start means attrition.</summary>
    public int ActiveEnd;

    /// <summary>Managed heap in megabytes at the end of the window.</summary>
    public float MonoHeapMb;

    /// <summary>Total allocated memory in megabytes at the end of the window.</summary>
    public float TotalAllocatedMb;
}

/// <summary>
/// Renders <see cref="PerfStressSample"/> rows as a table.
///
/// Deliberately one row per sample on a single line: the output has to survive being
/// copied out of the console into a report without being reformatted, because the
/// whole point of the exercise is that the numbers are quoted rather than retyped.
/// </summary>
public static class PerfStressReport
{
    /// <summary>Column layout for one sample line.</summary>
    private const string ScoreFormat =
        "tier={0} variant={1} frames={2} window={3}s avg={4}ms min={5}ms max={6}ms sd={7}ms " +
        "fps={8} gc={9}B/f memD={10}MB batches={11} setPass={12} draw={13} active={14}/{15} " +
        "mono={16}MB total={17}MB";

    /// <summary>
    /// Renders every sample as a header plus one line each, so the result can be read
    /// in the console and pasted into a report unchanged.
    /// </summary>
    /// <param name="samples">Rows to render, in run order.</param>
    /// <param name="warmUpSeconds">Warm-up used, recorded so the numbers stay interpretable.</param>
    /// <param name="sampleSeconds">Requested window length.</param>
    /// <param name="appliedVSync">Vertical sync in force during the run.</param>
    /// <param name="appliedTargetFrameRate">Frame-rate cap in force during the run.</param>
    /// <param name="profilerLogPath">Profiler log written, or null when none was.</param>
    /// <returns>The formatted table.</returns>
    public static string Build(IReadOnlyList<PerfStressSample> samples, float warmUpSeconds,
        float sampleSeconds, int appliedVSync, int appliedTargetFrameRate, string profilerLogPath)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("===== PerfStress baseline =====").Append('\n');
        builder.AppendFormat("scene='{0}' vSync={1} targetFrameRate={2} warmUp={3}s sample={4}s log={5}",
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                appliedVSync, appliedTargetFrameRate, warmUpSeconds, sampleSeconds,
                profilerLogPath ?? "(none)")
            .Append('\n');

        for (int i = 0; i < samples.Count; i++)
        {
            PerfStressSample sample = samples[i];
            builder.Append("[PerfStress] ").AppendFormat(ScoreFormat,
                    sample.Tier, sample.Variant, sample.Frames,
                    sample.WindowSeconds.ToString("F1"), sample.AverageMs.ToString("F2"),
                    sample.MinMs.ToString("F2"), sample.MaxMs.ToString("F2"),
                    sample.StdDevMs.ToString("F2"), sample.Fps.ToString("F1"),
                    sample.GcBytesPerFrame.ToString("F1"), sample.MemoryDeltaMb.ToString("F1"),
                    sample.Batches, sample.SetPassCalls, sample.DrawCalls,
                    sample.ActiveStart, sample.ActiveEnd,
                    sample.MonoHeapMb.ToString("F1"), sample.TotalAllocatedMb.ToString("F1"))
                .Append('\n');
        }

        builder.Append("===============================");
        return builder.ToString();
    }
}
