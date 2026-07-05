using NoKill.Core.Models;

namespace NoKill.Diagnostics.Tests;

public class WaitChainInterpreterTests
{
    private static WaitChainNode Thread(int tid, int pid = 100, WaitNodeStatus status = WaitNodeStatus.Blocked) =>
        new() { Kind = WaitNodeKind.Thread, Status = status, ProcessId = pid, ThreadId = tid };

    private static WaitChainNode Obj(
        WaitNodeKind kind, WaitNodeStatus status = WaitNodeStatus.Owned, string? name = null) =>
        new() { Kind = kind, Status = status, ObjectName = name };

    private static WaitChainReport Report(params ThreadWaitChain[] chains) => new()
    {
        ProcessId = 100,
        Chains = chains,
        CapturedAt = DateTimeOffset.Now,
    };

    private static ThreadWaitChain Chain(int tid, bool cycle, params WaitChainNode[] nodes) =>
        new() { ThreadId = tid, IsCycle = cycle, Nodes = nodes };

    [Fact]
    public void CycleChain_ProducesDeadlockInsight()
    {
        var report = Report(Chain(1, cycle: true,
            Thread(1), Obj(WaitNodeKind.Mutex), Thread(2), Obj(WaitNodeKind.Mutex), Thread(1)));

        var insights = WaitChainInterpreter.Interpret(report);

        Assert.Contains(insights, i => i.StartsWith("DEADLOCK"));
        Assert.True(report.DeadlockDetected);
    }

    [Fact]
    public void ChainCrossingProcessBoundary_NamesTheOtherProcess()
    {
        var report = Report(Chain(1, cycle: false,
            Thread(1), Obj(WaitNodeKind.Alpc), Thread(555, pid: 999)));

        var insights = WaitChainInterpreter.Interpret(report);

        var insight = Assert.Single(insights);
        Assert.Contains("ANOTHER PROCESS", insight);
        Assert.Contains("pid 999", insight);
    }

    [Fact]
    public void MutexHeldByAnotherThread_NamesTheHolder()
    {
        var report = Report(Chain(1, cycle: false,
            Thread(1), Obj(WaitNodeKind.Mutex, name: "MyAppLock"), Thread(42)));

        var insights = WaitChainInterpreter.Interpret(report);

        var insight = Assert.Single(insights);
        Assert.Contains("mutex", insight);
        Assert.Contains("'MyAppLock'", insight);
        Assert.Contains("held by thread 42", insight);
    }

    [Fact]
    public void AbandonedMutex_IsCalledOut()
    {
        var report = Report(Chain(1, cycle: false,
            Thread(1), Obj(WaitNodeKind.Mutex, WaitNodeStatus.Abandoned)));

        var insight = Assert.Single(WaitChainInterpreter.Interpret(report));
        Assert.Contains("ABANDONED", insight);
        Assert.Contains("owner died", insight);
    }

    [Fact]
    public void SendMessageWait_IsExplained()
    {
        var report = Report(Chain(1, cycle: false,
            Thread(1), Obj(WaitNodeKind.SendMessage, WaitNodeStatus.Blocked)));

        var insight = Assert.Single(WaitChainInterpreter.Interpret(report));
        Assert.Contains("SendMessage", insight);
    }

    [Fact]
    public void NothingBlocked_SaysSoHonestly()
    {
        // Single-node chains: threads exist but wait on nothing WCT can track.
        var report = Report(
            Chain(1, cycle: false, Thread(1, status: WaitNodeStatus.Running)),
            Chain(2, cycle: false, Thread(2, status: WaitNodeStatus.Blocked)));

        var insight = Assert.Single(WaitChainInterpreter.Interpret(report));
        Assert.Contains("No blocking wait chains detected", insight);
    }

    [Fact]
    public void Describe_RendersReadableChain()
    {
        var chain = Chain(1, cycle: false,
            Thread(1), Obj(WaitNodeKind.Mutex, name: "X"), Thread(2));

        string text = WaitChainInterpreter.Describe(chain);

        Assert.Equal("thread 1 [Blocked] → mutex 'X' [Owned] → thread 2 [Blocked]", text);
    }
}
