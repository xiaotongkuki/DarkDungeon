using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Play-mode tests for the damped camera follow: convergence on a displaced
/// target, Z preservation and the null-target guard. The target reference is
/// wired through SerializedObject exactly as the Inspector would.
/// </summary>
public class CameraFollow2DTests
{
    // ---- Test tuning ----
    // Waiting is time-based, not frame-based: a backgrounded editor runs the
    // player loop without vsync, so a fixed frame count can represent far less
    // game time than expected and would flake the convergence assertions.
    private const float ConvergeSeconds = 1f;   // >> SmoothDamp time constant (0.18 s): fully settled
    private const float PositionTolerance = 0.05f; // units: "converged" threshold

    private GameObject _target;
    private GameObject _camera;

    [SetUp]
    public void SetUp()
    {
        _target = new GameObject("Target");
        _camera = new GameObject("Camera");
        _camera.AddComponent<Camera>();
        var follow = _camera.AddComponent<CameraFollow2D>();

        // Inspector-equivalent wiring for the private serialized field.
        var so = new SerializedObject(follow);
        so.FindProperty("_target").objectReferenceValue = _target.transform;
        so.ApplyModifiedProperties();
    }

    [TearDown]
    public void TearDown()
    {
        Object.Destroy(_camera);
        Object.Destroy(_target);
    }

    /// <summary>
    /// Yields until the given amount of game time has passed, regardless of
    /// how many frames that takes on the current machine/editor focus state.
    /// </summary>
    private static IEnumerator WaitForGameTime(float seconds)
    {
        float deadline = Time.time + seconds;
        while (Time.time < deadline)
        {
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator Follow_TargetDisplaced_ConvergesToTarget()
    {
        _camera.transform.position = new Vector3(0f, 0f, -10f);
        _target.transform.position = new Vector3(10f, 5f, 0f);

        yield return WaitForGameTime(ConvergeSeconds);

        float distance = Vector2.Distance(_camera.transform.position, _target.transform.position);
        Assert.LessOrEqual(distance, PositionTolerance,
            "Camera must converge on the displaced target within the time budget.");
    }

    [UnityTest]
    public IEnumerator Follow_Converging_KeepsCameraZFixed()
    {
        const float initialZ = -10f;
        _camera.transform.position = new Vector3(0f, 0f, initialZ);
        _target.transform.position = new Vector3(10f, 5f, 0f);

        yield return WaitForGameTime(ConvergeSeconds);

        Assert.AreEqual(initialZ, _camera.transform.position.z,
            "Follow must never drift the camera depth.");
    }

    [UnityTest]
    public IEnumerator Follow_TargetNull_DoesNotMoveCamera()
    {
        // Rewire to no target: LateUpdate must early-out and leave the
        // camera where it is instead of throwing.
        var follow = _camera.GetComponent<CameraFollow2D>();
        var so = new SerializedObject(follow);
        so.FindProperty("_target").objectReferenceValue = null;
        so.ApplyModifiedProperties();

        Vector3 before = _camera.transform.position = new Vector3(3f, 2f, -10f);

        for (int i = 0; i < 10; i++)
        {
            yield return null;
        }

        Assert.AreEqual(before, _camera.transform.position,
            "A missing target must leave the camera untouched.");
    }
}
