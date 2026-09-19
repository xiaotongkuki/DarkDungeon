using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// The scene state a stress run has to take over, and the discipline to put it back.
///
/// A benchmark that leaves the game changed is worse than no benchmark, so every
/// value the run touches is recorded here first and restored afterwards. It also
/// holds the two interventions that make a load measurable at all:
/// <list type="bullet">
/// <item><description>Silencing the systems that would change the enemy count or
/// pause the run mid-window.</description></item>
/// <item><description>Making the player survive, so a crowd cannot deactivate it and
/// take the contact-damage callbacks down with it.</description></item>
/// </list>
///
/// Private state is reached by reflection on purpose: production components should
/// not grow test-only APIs just so a diagnostic tool can drive them.
/// </summary>
public sealed class PerfStressEnvironment
{
    /// <summary>Seconds to wait for the registry to drain between samples.</summary>
    public const float ClearTimeoutSeconds = 10f;

    /// <summary>Field holding the spawner's live cap.</summary>
    private const string MaxAliveField = "_maxAlive";

    /// <summary>Field holding the player's invulnerability deadline.</summary>
    private const string InvulnerableUntilField = "_invulnerableUntil";

    /// <summary>
    /// Invulnerability window pushed onto the player so the load cannot end the run.
    /// Contact damage still walks its full callback path; only the health arithmetic
    /// inside <c>TakeDamage</c> short-circuits, which is what keeps the callbacks
    /// being measured while the player survives.
    /// </summary>
    private const double ImmortalWindowSeconds = 1e9;

    private bool _captured;
    private int _vSyncOriginal;
    private int _targetFrameRateOriginal;
    private bool _autoAttackOriginal;
    private bool _gemSpawnerOriginal;
    private bool _xpTrackerOriginal;
    private bool _upgradeChoiceOriginal;
    private int _maxAliveOriginal;
    private double _invulnerableUntilOriginal;

    /// <summary>
    /// Records the values a run is about to change, so they can be put back.
    /// </summary>
    /// <param name="targets">Scene components the run will drive.</param>
    public void Capture(PerfStressHarness.Targets targets)
    {
        _vSyncOriginal = QualitySettings.vSyncCount;
        _targetFrameRateOriginal = Application.targetFrameRate;
        _autoAttackOriginal = targets.AutoAttack != null && targets.AutoAttack.enabled;
        _gemSpawnerOriginal = targets.GemSpawner != null && targets.GemSpawner.enabled;
        _xpTrackerOriginal = targets.XpTracker != null && targets.XpTracker.enabled;
        _upgradeChoiceOriginal = targets.UpgradeChoice != null && targets.UpgradeChoice.enabled;
        _maxAliveOriginal = ReadInt(targets.Spawner, MaxAliveField);
        _invulnerableUntilOriginal = ReadDouble(targets.Player, InvulnerableUntilField);
        _captured = true;
    }

    /// <summary>Vertical sync the run applied, for the report header.</summary>
    public int AppliedVSync { get; private set; }

    /// <summary>Frame-rate cap the run applied, for the report header.</summary>
    public int AppliedTargetFrameRate { get; private set; }

    /// <summary>
    /// Turns off everything that would make the measurement depend on the clock:
    /// vertical sync, the frame-rate cap, and any pause left over from a level-up.
    ///
    /// Without the first two the frame time is clamped to the display refresh and
    /// every tier reports the same frame rate, which is the one way this harness can
    /// silently produce a meaningless result.
    ///
    /// The applied values are remembered because the header has to describe the run,
    /// not the state after the run put everything back.
    /// </summary>
    public void ApplyMeasurementSettings()
    {
        AppliedVSync = 0;
        AppliedTargetFrameRate = -1;

        QualitySettings.vSyncCount = AppliedVSync;
        Application.targetFrameRate = AppliedTargetFrameRate;
        Application.runInBackground = true;
        Time.timeScale = 1f;
    }

    /// <summary>
    /// Puts the clock settings back exactly as they were found.
    /// </summary>
    public void Restore()
    {
        if (!_captured)
        {
            return;
        }

        QualitySettings.vSyncCount = _vSyncOriginal;
        Application.targetFrameRate = _targetFrameRateOriginal;
        Time.timeScale = 1f;
    }

    /// <summary>
    /// Stops the systems that would change the enemy count or interrupt the run, or
    /// restores their previous state.
    ///
    /// The spawner is held at a live cap of zero rather than disabled, because a
    /// disabled component never runs the reference resolution its own spawn method
    /// depends on. The gem, experience and upgrade systems are switched off so a kill
    /// cannot drop a gem, and a gem cannot raise a level-up that would set the time
    /// scale to zero in the middle of a window.
    /// </summary>
    /// <param name="targets">Scene components to silence.</param>
    /// <param name="silence">True to silence them, false to restore.</param>
    public void SilenceAttrition(PerfStressHarness.Targets targets, bool silence)
    {
        WritePrivateField(targets.Spawner, MaxAliveField, silence ? 0 : _maxAliveOriginal);

        SetEnabled(targets.AutoAttack, silence ? false : _autoAttackOriginal);
        SetEnabled(targets.GemSpawner, silence ? false : _gemSpawnerOriginal);
        SetEnabled(targets.XpTracker, silence ? false : _xpTrackerOriginal);
        SetEnabled(targets.UpgradeChoice, silence ? false : _upgradeChoiceOriginal);
    }

    /// <summary>
    /// Pushes the player's invulnerability deadline far into the future, or puts the
    /// original back.
    ///
    /// A thousand enemies kill the player within a couple of seconds, and a dead
    /// player deactivates its object, which stops the very trigger callbacks the
    /// contact-damage variant exists to measure. Keeping the player alive keeps the
    /// load shape honest.
    /// </summary>
    /// <param name="targets">Scene components; the player may be null.</param>
    /// <param name="immortal">True to make the player survive the run.</param>
    public void SetPlayerImmortal(PerfStressHarness.Targets targets, bool immortal)
    {
        if (targets.Player == null)
        {
            return;
        }

        double deadline = immortal
            ? Time.timeAsDouble + ImmortalWindowSeconds
            : _invulnerableUntilOriginal;
        WritePrivateField(targets.Player, InvulnerableUntilField, deadline);
    }

    /// <summary>
    /// Starts a binary profiler log next to the project, so the same run can be
    /// reloaded into the Profiler window for a timeline screenshot.
    /// </summary>
    /// <param name="logFolder">Folder name, relative to the project root.</param>
    /// <returns>Full path of the log being written.</returns>
    public string StartProfilerLog(string logFolder)
    {
        string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", logFolder));
        System.IO.Directory.CreateDirectory(folder);

        string file = System.IO.Path.Combine(folder,
            string.Format("perf_{0:yyyyMMdd_HHmmss}.raw", DateTime.Now));

        Profiler.logFile = file;
        Profiler.enableBinaryLog = true;
        Profiler.enabled = true;
        return file;
    }

    /// <summary>
    /// Stops recording and flushes the log.
    /// </summary>
    public void StopProfilerLog()
    {
        Profiler.enabled = false;
        Profiler.enableBinaryLog = false;
        Profiler.logFile = null;
    }

    /// <summary>
    /// Reads the editor's rendering counters. Reports -1 outside the editor, where the
    /// API does not exist.
    /// </summary>
    /// <param name="batches">Receives the sprite batch count.</param>
    /// <param name="setPass">Receives the SetPass call count.</param>
    /// <param name="drawCalls">Receives the draw call count.</param>
    public static void ReadRenderStats(out int batches, out int setPass, out int drawCalls)
    {
#if UNITY_EDITOR
        batches = UnityStats.batches;
        setPass = UnityStats.setPassCalls;
        drawCalls = UnityStats.drawCalls;
#else
        batches = -1;
        setPass = -1;
        drawCalls = -1;
#endif
    }

    /// <summary>
    /// Sets a component's enabled flag, tolerating a null reference.
    /// </summary>
    /// <param name="component">Component to change, or null.</param>
    /// <param name="value">Whether it should be enabled.</param>
    private static void SetEnabled(Behaviour component, bool value)
    {
        if (component != null)
        {
            component.enabled = value;
        }
    }

    /// <summary>
    /// Reads a private int field, tolerating a missing field or component.
    /// </summary>
    /// <param name="target">Object owning the field; null yields 0.</param>
    /// <param name="fieldName">Serialized field name.</param>
    private static int ReadInt(object target, string fieldName)
    {
        object value = ReadField(target, fieldName);
        return value != null ? Convert.ToInt32(value) : 0;
    }

    /// <summary>
    /// Reads a private double field, tolerating a missing field or component.
    /// </summary>
    /// <param name="target">Object owning the field; null yields 0.</param>
    /// <param name="fieldName">Serialized field name.</param>
    private static double ReadDouble(object target, string fieldName)
    {
        object value = ReadField(target, fieldName);
        return value != null ? Convert.ToDouble(value) : 0d;
    }

    /// <summary>
    /// Reads a private instance field by name.
    /// </summary>
    /// <param name="target">Object owning the field; null yields null.</param>
    /// <param name="fieldName">Field name.</param>
    private static object ReadField(object target, string fieldName)
    {
        if (target == null)
        {
            return null;
        }

        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        return field != null ? field.GetValue(target) : null;
    }

    /// <summary>
    /// Writes a private instance field by name, doing nothing when the field or the
    /// object is missing.
    /// </summary>
    /// <param name="target">Object owning the field.</param>
    /// <param name="fieldName">Field name.</param>
    /// <param name="value">Value to write.</param>
    private static void WritePrivateField(object target, string fieldName, object value)
    {
        if (target == null)
        {
            return;
        }

        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(target, value);
    }
}
