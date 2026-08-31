using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

public class DroneDiagnosticsTests
{
    [TearDown]
    public void TearDown()
    {
        DroneDiagnostics.LifecycleLoggingEnabled = false;
    }

    [Test]
    public void LifecycleLoggingIsSilentWhenDisabled()
    {
        DroneDiagnostics.LifecycleLoggingEnabled = false;

        DroneDiagnostics.LogTaskCancelled(Entity.Null);

        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void LifecycleLoggingIncludesStandardPrefixAndTaskContextWhenEnabled()
    {
        DroneDiagnostics.LifecycleLoggingEnabled = true;
        LogAssert.Expect(
            LogType.Log,
            $"[DroneLifecycle] Cancelled task. Task : {Entity.Null}");

        DroneDiagnostics.LogTaskCancelled(Entity.Null);
    }
}
