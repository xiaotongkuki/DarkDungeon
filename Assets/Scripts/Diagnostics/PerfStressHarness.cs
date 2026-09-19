using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Measures the game under a controlled enemy load, so performance claims rest on
/// numbers rather than impressions.
///
/// Every interesting cost in this project is load-dependent: per-enemy AI and
/// physics, the spatial grid, the trigger callbacks that fire for each enemy
/// overlapping the player, and the sprite batches. None of that can be judged from a
/// quiet scene, and measuring it by hand is neither repeatable nor comparable.
///
/// It drives the real scene - real prefabs, real data assets, the real spawn ring -
/// because a synthetic fixture would hide exactly the integration costs the numbers
/// are meant to capture.
///
/// Two rules shape every sample:
/// <list type="bullet">
/// <item><description>The fill and the warm-up sit outside the measured window, so an
/// instantiate spike is never reported as steady-state cost.</description></item>
/// <item><description>Variant switches are applied <i>after</i> the warm-up, so every
/// variant is measured in the same converged enemy distribution and the difference
/// between variants is real cost rather than a layout artefact.</description></item>
/// </list>
///
/// Environment work lives in <see cref="PerfStressEnvironment"/>, the table in
/// <see cref="PerfStressReport"/>; this class only orchestrates.
/// </summary>
public class PerfStressHarness : MonoBehaviour
{
    /// <summary>
    /// Which cost centres stay alive during a sample.
    ///
    /// Each value is a complete configuration rather than an increment on the
    /// previous one, so samples never depend on the order they run in.
    /// </summary>
    public enum StressVariant
    {
        /// <summary>Everything on except the auto-attack, which would thin the load.</summary>
        Full = 0,

        /// <summary>Contact damage off: isolates the per-overlap trigger callbacks.</summary>
        NoContactDamage = 1,

        /// <summary>Chase off as well, frozen after convergence: isolates movement and physics.</summary>
        NoAi = 2,

        /// <summary>Sprites off as well: isolates rendering.</summary>
        NoRender = 3,

        /// <summary>Auto-attack on: measures the projectile and spatial-grid query cost.</summary>
        WithAutoAttack = 4,
    }

    /// <summary>
    /// Scene components the harness drives. Filled in by the caller, because the
    /// harness is attached at runtime and cannot serialize scene references.
    /// </summary>
    public struct Targets
    {
        /// <summary>Spawner used to build the load, and the owner of the live cap.</summary>
        public EnemySpawner Spawner;

        /// <summary>Registry queried for the live count.</summary>
        public EnemyManager Manager;

        /// <summary>Player whose death would end the run mid-sample.</summary>
        public PlayerHealth Player;

        /// <summary>Auto-attack, enabled only by the auto-attack variant.</summary>
        public PlayerAutoAttack AutoAttack;

        /// <summary>Drop source, silenced so no gem is created during a run.</summary>
        public GemSpawner GemSpawner;

        /// <summary>Experience tracker, silenced so no level-up can pause the run.</summary>
        public XpTracker XpTracker;

        /// <summary>Upgrade panel, silenced for the same reason.</summary>
        public UpgradeChoiceController UpgradeChoice;
    }

    /// <summary>Bytes in a megabyte, for the memory columns.</summary>
    private const float BytesPerMegabyte = 1048576f;

    /// <summary>
    /// Frame rate held during fill and warm-up, where nothing is being measured.
    ///
    /// Convergence is driven by the fixed timestep, so slowing the frame rate costs
    /// the sample nothing, and an open Profiler window is spared thousands of
    /// uninteresting frames before the measurement even begins.
    /// </summary>
    private const int WarmUpFrameRate = 60;

    /// <summary>Frame rate during the measured window; -1 means uncapped.</summary>
    private const int UncappedFrameRate = -1;

    [Header("Scale")]
    [Tooltip("Enemy counts to measure; one sample per tier.")]
    [SerializeField] private int[] _tiers = { 200, 500, 1000 };
    [Tooltip("Enemies created per frame while filling a tier. Keeps a thousand-enemy fill from " +
             "stalling the editor inside a single frame.")]
    [SerializeField] private int _spawnBatchPerFrame = 100;

    [Header("Timing")]
    [Tooltip("Seconds to let the load settle before sampling. Enemies spawn outside the view and " +
             "converge on the player; every variant must be measured in that same converged shape.")]
    [SerializeField] private float _warmUpSeconds = 5f;
    [Tooltip("Seconds of steady-state sampling per sample. Fill and warm-up are excluded on purpose.")]
    [SerializeField] private float _sampleSeconds = 5f;
    [Tooltip("Hard frame budget for one sample, whichever of the two limits is reached first. " +
             "The frame rate is deliberately uncapped so a frame delta means real cost, which " +
             "means a quiet scene can produce hundreds of frames a second; without this budget an " +
             "open Profiler window would have to hold every one of them and can run the machine " +
             "out of memory. A window that ends early is visible in the report as a window " +
             "shorter than the requested sample.")]
    [SerializeField] private int _maxSampleFrames = 6000;

    [Header("Variants")]
    [Tooltip("Variants to measure, in order.")]
    [SerializeField] private StressVariant[] _variants = { StressVariant.Full };

    [Header("Output")]
    [Tooltip("Record a .raw profiler log so the run can be reloaded into the Profiler window for " +
             "screenshots. Recording adds overhead, so switch it off when the numbers matter more " +
             "than the timeline.")]
    [SerializeField] private bool _captureProfilerLog = true;
    [Tooltip("Folder for the profiler log, relative to the project root. Deliberately outside " +
             "Assets so Unity does not try to import it.")]
    [SerializeField] private string _logFolder = "PerfLogs";

    /// <summary>True once the run has finished and the environment has been restored.</summary>
    public bool IsDone { get; private set; }

    /// <summary>Table built by the run; empty until <see cref="IsDone"/>.</summary>
    public string Report { get; private set; }

    /// <summary>Every measured sample, in run order.</summary>
    public IReadOnlyList<PerfStressSample> Results => _results;

    /// <summary>Path of the recorded profiler log, or null when none was written.</summary>
    public string ProfilerLogPath { get; private set; }

    private readonly List<PerfStressSample> _results = new List<PerfStressSample>();
    private readonly List<Enemy> _live = new List<Enemy>();
    private readonly PerfStressEnvironment _environment = new PerfStressEnvironment();

    private Targets _targets;
    private bool _running;

    /// <summary>
    /// Overrides the tuning fields, so a caller can drive the harness without
    /// touching serialized state. Call before <see cref="Begin"/>.
    /// </summary>
    /// <param name="tiers">Enemy counts to measure; null or empty keeps the current setting.</param>
    /// <param name="variants">Variants to measure; null or empty keeps the current setting.</param>
    /// <param name="captureProfilerLog">Whether to record a .raw profiler log.</param>
    /// <param name="warmUpSeconds">Convergence window; zero or less keeps the current setting.</param>
    /// <param name="sampleSeconds">Measured window; zero or less keeps the current setting.</param>
    public void Configure(int[] tiers, StressVariant[] variants, bool captureProfilerLog,
        float warmUpSeconds = 0f, float sampleSeconds = 0f)
    {
        if (tiers != null && tiers.Length > 0)
        {
            _tiers = tiers;
        }

        if (variants != null && variants.Length > 0)
        {
            _variants = variants;
        }

        if (warmUpSeconds > 0f)
        {
            _warmUpSeconds = warmUpSeconds;
        }

        if (sampleSeconds > 0f)
        {
            _sampleSeconds = sampleSeconds;
        }

        _captureProfilerLog = captureProfilerLog;
    }

    /// <summary>
    /// Starts the run. Returns immediately; poll <see cref="IsDone"/> for completion.
    /// </summary>
    /// <param name="targets">Scene components to drive.</param>
    public void Begin(Targets targets)
    {
        if (_running)
        {
            Debug.LogWarning($"PerfStressHarness on '{name}' is already running.", this);
            return;
        }

        _targets = targets;
        _results.Clear();
        Report = string.Empty;
        ProfilerLogPath = null;
        IsDone = false;
        _running = true;

        StartCoroutine(RunAll());
    }

    /// <summary>
    /// Runs every tier and variant, then restores the environment and publishes the
    /// report. One coroutine, so the restore path runs even when a sample aborts.
    /// </summary>
    private IEnumerator RunAll()
    {
        _environment.Capture(_targets);
        _environment.ApplyMeasurementSettings();
        _environment.SilenceAttrition(_targets, true);
        _environment.SetPlayerImmortal(_targets, true);

        // An open Profiler window records every frame, and the frame rate here is
        // deliberately uncapped so a frame delta means real cost - which also means a
        // quiet scene hands the Profiler hundreds of frames a second. Warn rather than
        // let a machine with little headroom run itself out of memory without saying
        // why.
        if (Profiler.enabled)
        {
            Debug.LogWarning($"PerfStressHarness on '{name}': the Profiler is recording. At an uncapped " +
                             $"frame rate that is hundreds of frames per second, so each sample is capped " +
                             $"at {_maxSampleFrames} frames. Close memory-heavy applications if the editor " +
                             $"reports out of memory.", this);
        }

        if (_captureProfilerLog)
        {
            ProfilerLogPath = _environment.StartProfilerLog(_logFolder);
        }

        for (int t = 0; t < _tiers.Length; t++)
        {
            for (int v = 0; v < _variants.Length; v++)
            {
                yield return RunSample(_tiers[t], _variants[v]);
            }
        }

        if (_captureProfilerLog)
        {
            _environment.StopProfilerLog();
        }

        _environment.SetPlayerImmortal(_targets, false);
        _environment.SilenceAttrition(_targets, false);
        _environment.Restore();

        Report = PerfStressReport.Build(_results, _warmUpSeconds, _sampleSeconds,
            _environment.AppliedVSync, _environment.AppliedTargetFrameRate, ProfilerLogPath);
        Debug.Log(Report, this);

        _running = false;
        IsDone = true;
    }

    /// <summary>
    /// Fills the tier, lets it converge, applies the variant, measures the steady
    /// window and then drains the load again.
    /// </summary>
    /// <param name="count">Enemies to hold during the window.</param>
    /// <param name="variant">Which cost centres to leave running.</param>
    private IEnumerator RunSample(int count, StressVariant variant)
    {
        // Fill and warm-up are held to a sane frame rate: nothing here is measured, so
        // the only thing an uncapped rate would achieve is filling the Profiler's
        // buffer with frames nobody will look at.
        Application.targetFrameRate = WarmUpFrameRate;

        yield return Fill(count);

        // Convergence has to happen before the variant is applied: switching the
        // chase off earlier would freeze the enemies out on the spawn ring, and the
        // sample would then describe a distribution no other variant shares.
        yield return WaitUnscaled(_warmUpSeconds);
        ApplyVariant(variant);

        // Uncapped for the window itself, because a frame delta only means real cost
        // when nothing is holding the frame rate down.
        Application.targetFrameRate = UncappedFrameRate;

        int activeStart = ActiveCount();
        long gcAtStart = System.GC.GetAllocatedBytesForCurrentThread();
        long memoryAtStart = Profiler.GetTotalAllocatedMemoryLong();
        float windowStart = Time.unscaledTime;

        int frames = 0;
        float sum = 0f;
        float min = float.MaxValue;
        float max = 0f;
        double sumOfSquares = 0d;
        long batches = 0;
        long setPass = 0;
        long drawCalls = 0;

        while (Time.unscaledTime - windowStart < _sampleSeconds && frames < _maxSampleFrames)
        {
            float dt = Time.unscaledDeltaTime;
            frames++;
            sum += dt;
            sumOfSquares += (double)dt * dt;
            if (dt < min)
            {
                min = dt;
            }
            if (dt > max)
            {
                max = dt;
            }

            PerfStressEnvironment.ReadRenderStats(out int frameBatches, out int frameSetPass,
                out int frameDraws);
            batches += frameBatches;
            setPass += frameSetPass;
            drawCalls += frameDraws;

            yield return null;
        }

        // The wall-clock length is measured rather than assumed: the window is driven
        // by unscaled time, and in the editor that can run well past the requested
        // seconds when the editor throttles a background window. A window shorter than
        // the requested one means the frame budget ended it first.
        float windowSeconds = Time.unscaledTime - windowStart;
        long gcBytes = System.GC.GetAllocatedBytesForCurrentThread() - gcAtStart;
        long memoryDelta = Profiler.GetTotalAllocatedMemoryLong() - memoryAtStart;

        _results.Add(BuildSample(count, variant, frames, windowSeconds, sum, min, max, sumOfSquares,
            gcBytes, memoryDelta, batches, setPass, drawCalls, activeStart, ActiveCount()));

        yield return Drain();
    }

    /// <summary>
    /// Folds one measured window into a sample row.
    ///
    /// The derived maths lives here, next to the loop that produced the raw counters,
    /// so a reader can check each figure against the loop without jumping files.
    /// </summary>
    /// <param name="count">Tier the window was meant to hold.</param>
    /// <param name="variant">Variant measured.</param>
    /// <param name="frames">Frames in the window.</param>
    /// <param name="windowSeconds">Real seconds the window lasted.</param>
    /// <param name="sum">Sum of frame times, in seconds.</param>
    /// <param name="min">Fastest frame, in seconds.</param>
    /// <param name="max">Slowest frame, in seconds.</param>
    /// <param name="sumOfSquares">Sum of squared frame times, for the deviation.</param>
    /// <param name="gcBytes">Managed bytes allocated during the window.</param>
    /// <param name="memoryDelta">Change in total allocated memory over the window, in bytes.</param>
    /// <param name="batches">Summed sprite batches over the window.</param>
    /// <param name="setPass">Summed SetPass calls over the window.</param>
    /// <param name="drawCalls">Summed draw calls over the window.</param>
    /// <param name="activeStart">Live enemies when the window opened.</param>
    /// <param name="activeEnd">Live enemies when the window closed.</param>
    /// <returns>The finished row.</returns>
    private static PerfStressSample BuildSample(int count, StressVariant variant, int frames,
        float windowSeconds, float sum, float min, float max, double sumOfSquares, long gcBytes,
        long memoryDelta, long batches, long setPass, long drawCalls, int activeStart, int activeEnd)
    {
        float average = frames > 0 ? sum / frames : 0f;
        float variance = frames > 0 ? (float)(sumOfSquares / frames) - average * average : 0f;

        return new PerfStressSample
        {
            Tier = count,
            Variant = variant,
            Frames = frames,
            WindowSeconds = windowSeconds,
            AverageMs = average * 1000f,
            MinMs = min == float.MaxValue ? 0f : min * 1000f,
            MaxMs = max * 1000f,
            StdDevMs = variance > 0f ? Mathf.Sqrt(variance) * 1000f : 0f,

            // Frames over real seconds, not the reciprocal of the mean player-loop delta:
            // those two differ by the editor's own per-frame cost, and the reciprocal
            // would overstate the rate.
            Fps = windowSeconds > 0f ? frames / windowSeconds : 0f,
            GcBytesPerFrame = frames > 0 ? (float)gcBytes / frames : 0f,
            MemoryDeltaMb = memoryDelta / BytesPerMegabyte,
            Batches = frames > 0 ? (int)(batches / frames) : -1,
            SetPassCalls = frames > 0 ? (int)(setPass / frames) : -1,
            DrawCalls = frames > 0 ? (int)(drawCalls / frames) : -1,
            ActiveStart = activeStart,
            ActiveEnd = activeEnd,
            MonoHeapMb = Profiler.GetMonoHeapSizeLong() / BytesPerMegabyte,
            TotalAllocatedMb = Profiler.GetTotalAllocatedMemoryLong() / BytesPerMegabyte,
        };
    }

    /// <summary>
    /// Creates enemies through the scene's own spawner, in batches across frames so a
    /// large fill cannot lock the editor.
    ///
    /// It fills <i>to</i> the tier rather than adding to it: the scene's spawner runs
    /// from the moment the scene loads, so a few enemies can already exist by the time
    /// the run begins, and a tier that is "about two hundred" is not a tier.
    /// </summary>
    /// <param name="count">Total enemies the tier should hold.</param>
    private IEnumerator Fill(int count)
    {
        int missing = Mathf.Max(0, count - ActiveCount());
        int created = 0;
        while (created < missing)
        {
            int batch = Mathf.Min(_spawnBatchPerFrame, missing - created);
            for (int i = 0; i < batch; i++)
            {
                Enemy enemy = _targets.Spawner != null ? _targets.Spawner.SpawnOne() : null;
                if (enemy == null)
                {
                    Debug.LogError($"PerfStressHarness on '{name}' could not spawn enemy " +
                                   $"{created + i + 1} of {missing}; check the spawner's table.", this);
                    yield break;
                }

                _live.Add(enemy);
            }

            created += batch;
            yield return null;
        }
    }

    /// <summary>
    /// Destroys the live load and waits for the registry to drain, so the next sample
    /// starts from an empty scene.
    /// </summary>
    private IEnumerator Drain()
    {
        for (int i = 0; i < _live.Count; i++)
        {
            if (_live[i] != null)
            {
                Destroy(_live[i].gameObject);
            }
        }
        _live.Clear();

        // Destruction is deferred to the end of the frame and each enemy queues its
        // own grid removal from OnDisable, so both need a frame and a flush.
        yield return null;
        _targets.Manager?.FlushRemovals();

        float deadline = Time.unscaledTime + PerfStressEnvironment.ClearTimeoutSeconds;
        while (ActiveCount() > 0 && Time.unscaledTime < deadline)
        {
            _targets.Manager?.FlushRemovals();
            yield return null;
        }
    }

    /// <summary>
    /// Applies one variant's switches to the scene and to every live enemy.
    ///
    /// Scene-level switches are assigned outright rather than toggled, so a variant
    /// can never inherit the previous sample's state.
    /// </summary>
    /// <param name="variant">Variant to apply.</param>
    private void ApplyVariant(StressVariant variant)
    {
        if (_targets.AutoAttack != null)
        {
            _targets.AutoAttack.enabled = variant == StressVariant.WithAutoAttack;
        }

        bool render = variant != StressVariant.NoRender;
        bool chase = variant != StressVariant.NoAi && render;
        bool contactDamage = variant != StressVariant.NoContactDamage && chase;

        for (int i = 0; i < _live.Count; i++)
        {
            Enemy enemy = _live[i];
            if (enemy == null)
            {
                continue;
            }

            SetEnabled(enemy.GetComponent<EnemyContactDamage>(), contactDamage);
            SetEnabled(enemy.GetComponent<EnemyAI>(), chase);
            SetEnabled(enemy.GetComponent<SpriteRenderer>(), render);
        }
    }

    /// <summary>
    /// Enables or disables a behaviour, tolerating enemies whose prefab lacks it.
    /// </summary>
    /// <param name="component">Component to toggle, or null.</param>
    /// <param name="value">Whether it should be enabled.</param>
    private static void SetEnabled(Behaviour component, bool value)
    {
        if (component != null)
        {
            component.enabled = value;
        }
    }

    /// <summary>
    /// Enables or disables a renderer.
    ///
    /// A separate overload rather than a shared constraint: <see cref="Renderer"/>
    /// derives from <see cref="Component"/> and not from <see cref="Behaviour"/>, so
    /// the two have no common type that exposes <c>enabled</c>.
    /// </summary>
    /// <param name="renderer">Renderer to toggle, or null.</param>
    /// <param name="value">Whether it should be enabled.</param>
    private static void SetEnabled(Renderer renderer, bool value)
    {
        if (renderer != null)
        {
            renderer.enabled = value;
        }
    }

    /// <summary>
    /// Current live enemy count, or 0 when the registry is absent.
    /// </summary>
    private int ActiveCount()
    {
        return _targets.Manager != null ? _targets.Manager.ActiveCount : 0;
    }

    /// <summary>
    /// Waits in unscaled time, so a window is measured in real seconds regardless of
    /// the time scale.
    /// </summary>
    /// <param name="seconds">Real seconds to wait.</param>
    private static IEnumerator WaitUnscaled(float seconds)
    {
        float deadline = Time.unscaledTime + seconds;
        while (Time.unscaledTime < deadline)
        {
            yield return null;
        }
    }
}
