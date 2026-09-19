using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Drives <see cref="PerfStressHarness"/> against the real game scene.
///
/// It loads <c>Game.unity</c> rather than assembling a fixture, for two reasons: the
/// numbers are supposed to describe the shipping scene, and a scene-level smoke test
/// was the one gap the unit suites left open - this covers both at once.
///
/// Marked <see cref="ExplicitAttribute"/> on purpose. It takes tens of seconds and its
/// output depends on the machine, so it must never run as part of the normal suite;
/// it is invoked by name when a baseline is wanted.
///
/// Nothing here asserts on performance. Frame times vary with the machine and the
/// editor's mood, so a threshold would only produce noise; the numbers are reported
/// and the assertions cover the things that must be true for the numbers to mean
/// anything.
///
/// <b>Give the editor memory before running it.</b> The measured window is uncapped, so
/// a quiet scene runs at several hundred frames a second, and an open Profiler window
/// has to record every one of them. On a machine with little free memory the engine
/// reports "the system is running out of memory" and the run dies partway. The harness
/// bounds its own footprint (frame budget, throttled warm-up); the rest is up to what
/// else is running.
/// </summary>
[Category("Performance")]
public class PerfStressTests
{
    /// <summary>Scene to measure. Registered as build index 1.</summary>
    private const string GameSceneName = "Game";

    /// <summary>Enemy count for the smoke run.</summary>
    private const int SmokeTier = 200;

    /// <summary>Seconds allowed for enemies to converge before the window opens.</summary>
    private const float WarmUpSeconds = 5f;

    /// <summary>
    /// Measured window. Longer than the harness default so the run can be watched in
    /// the Profiler while it happens.
    /// </summary>
    private const float SampleSeconds = 20f;

    /// <summary>Wall-clock budget for the whole run, so a stuck harness fails loudly.</summary>
    private const float TimeoutSeconds = 300f;

    private GameObject _host;

    /// <summary>
    /// Removes the harness and clears the pause flag, so a failed run cannot leak into
    /// the next test through global state.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (_host != null)
        {
            Object.DestroyImmediate(_host);
            _host = null;
        }

        Time.timeScale = 1f;
    }

    /// <summary>
    /// Loads the game scene, runs one tier of the full-cost variant, and reports the
    /// measurement table.
    /// </summary>
    [Explicit]
    [UnityTest]
    public IEnumerator Baseline_SmokeRunReportsOneTier()
    {
        yield return SceneManager.LoadSceneAsync(GameSceneName, LoadSceneMode.Single);

        // One frame for Awake, OnEnable and Start to run before anything is looked up.
        yield return null;

        EnemySpawner spawner = Object.FindObjectOfType<EnemySpawner>();
        EnemyManager manager = Object.FindObjectOfType<EnemyManager>();
        PlayerHealth player = Object.FindObjectOfType<PlayerHealth>();
        PlayerAutoAttack autoAttack = Object.FindObjectOfType<PlayerAutoAttack>();
        GemSpawner gemSpawner = Object.FindObjectOfType<GemSpawner>();
        XpTracker xpTracker = Object.FindObjectOfType<XpTracker>();
        UpgradeChoiceController upgradeChoice = Object.FindObjectOfType<UpgradeChoiceController>();

        // These lookups are also the scene smoke test: a scene that lost one of these
        // components would otherwise only be noticed by playing it by hand.
        Assert.IsNotNull(spawner, "The game scene has no EnemySpawner, so no load can be built.");
        Assert.IsNotNull(manager, "The game scene has no EnemyManager, so no enemy can be counted.");
        Assert.IsNotNull(player, "The game scene has no PlayerHealth.");
        Assert.IsNotNull(autoAttack, "The game scene has no PlayerAutoAttack.");
        Assert.IsNotNull(gemSpawner, "The game scene has no GemSpawner.");
        Assert.IsNotNull(xpTracker, "The game scene has no XpTracker.");
        Assert.IsNotNull(upgradeChoice, "The game scene has no UpgradeChoiceController.");

        _host = new GameObject("PerfStressHarness");
        PerfStressHarness harness = _host.AddComponent<PerfStressHarness>();

        // The profiler log stays off for this run: recording adds overhead, and the
        // point of the smoke run is to watch the CPU trace live rather than to keep an
        // artifact.
        harness.Configure(
            new[] { SmokeTier },
            new[] { PerfStressHarness.StressVariant.Full },
            captureProfilerLog: false,
            warmUpSeconds: WarmUpSeconds,
            sampleSeconds: SampleSeconds);

        harness.Begin(new PerfStressHarness.Targets
        {
            Spawner = spawner,
            Manager = manager,
            Player = player,
            AutoAttack = autoAttack,
            GemSpawner = gemSpawner,
            XpTracker = xpTracker,
            UpgradeChoice = upgradeChoice,
        });

        float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        while (!harness.IsDone && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        Assert.IsTrue(harness.IsDone,
            $"The harness did not finish within {TimeoutSeconds} seconds.");
        Assert.IsNotEmpty(harness.Results, "The run produced no sample.");

        foreach (PerfStressSample sample in harness.Results)
        {
            // A tier that did not hold its count describes something other than the
            // load it claims to, so this is the one number worth asserting on.
            Assert.AreEqual(sample.Tier, sample.ActiveStart,
                $"Tier {sample.Tier} started with {sample.ActiveStart} live enemies.");
        }

        Assert.IsTrue(player.IsAlive,
            "The player died during the run, which stops the contact-damage callbacks being measured.");
        Assert.AreEqual(1f, Time.timeScale,
            "The run left the game paused, which would have frozen the measurement.");

        // Printed as well as logged by the harness, so the table lands in the test
        // report as well as in the console.
        TestContext.WriteLine(harness.Report);
    }
}
