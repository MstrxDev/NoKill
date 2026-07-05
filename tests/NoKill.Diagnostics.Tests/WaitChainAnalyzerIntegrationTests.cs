using System.Runtime.InteropServices;
using NoKill.Core.Models;

namespace NoKill.Diagnostics.Tests;

/// <summary>
/// Live integration test: builds a REAL two-thread mutex deadlock inside this
/// test process and verifies Wait Chain Traversal sees it. Background threads
/// keep the deadlock from outliving the test run.
/// </summary>
public class WaitChainAnalyzerIntegrationTests
{
    // Raw Win32 mutex waits, bypassing all .NET wait machinery, so the test
    // exercises exactly the wait shape WCT is documented to attribute.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateMutexW(nint attributes, bool initialOwner, string? name);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint timeoutMs);

    private const uint Infinite = 0xFFFFFFFF;

    [Fact]
    public void RealMutexDeadlock_IsDetectedInOwnProcess()
    {
        nint mutexA = CreateMutexW(0, false, null);
        nint mutexB = CreateMutexW(0, false, null);
        using var aHeld = new ManualResetEventSlim();
        using var bHeld = new ManualResetEventSlim();

        var t1 = new Thread(() =>
        {
            WaitForSingleObject(mutexA, Infinite);
            aHeld.Set();
            bHeld.Wait();
            WaitForSingleObject(mutexB, Infinite); // holds A, wants B
        })
        { IsBackground = true };

        var t2 = new Thread(() =>
        {
            WaitForSingleObject(mutexB, Infinite);
            bHeld.Set();
            aHeld.Wait();
            WaitForSingleObject(mutexA, Infinite); // holds B, wants A — cycle complete
        })
        { IsBackground = true };

        t1.Start();
        t2.Start();
        Assert.True(aHeld.Wait(5000) && bHeld.Wait(5000), "deadlock setup did not engage");
        Thread.Sleep(500); // both threads reach their final blocking wait

        var report = new WaitChainAnalyzer().Analyze(Environment.ProcessId);

        Assert.NotNull(report);

        // Diagnostic dump on failure: show what WCT actually returned
        string dump = string.Join("\n", report.Chains.Select(c =>
            $"tid {c.ThreadId} cycle={c.IsCycle}: {WaitChainInterpreter.Describe(c)}"));

        Assert.True(report.DeadlockDetected, $"no deadlock cycle found. Chains:\n{dump}");

        var cycleChain = report.Chains.First(c => c.IsCycle);
        Assert.Contains(cycleChain.Nodes, n => n.Kind == WaitNodeKind.Mutex);
    }
}
