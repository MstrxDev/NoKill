using System.Text;
using NoKill.Core.Models;

namespace NoKill.Diagnostics;

/// <summary>
/// Pure logic that turns raw wait chains into plain-English insights a user
/// can act on. No Win32 — fully unit-testable with hand-built chains. Honest
/// by design: when WCT sees nothing blocking, it says so instead of guessing
/// (events and .NET Monitor locks have no kernel-tracked owner, so a blocked
/// chain can legitimately come back empty).
/// </summary>
public static class WaitChainInterpreter
{
    public static IReadOnlyList<string> Interpret(WaitChainReport report)
    {
        var insights = new List<string>();

        // Deadlock cycles first: the most important finding there is.
        foreach (var chain in report.Chains.Where(c => c.IsCycle))
        {
            insights.Add($"DEADLOCK: wait cycle detected — {Describe(chain)}");
        }

        foreach (var chain in report.Chains.Where(c => !c.IsCycle && c.IsBlockedOnSomething))
        {
            // Waiting on another process is the "handle the blocker, not the
            // victim" case: the fix usually lives in the other process.
            var external = chain.Nodes.FirstOrDefault(n =>
                n.Kind == WaitNodeKind.Thread && n.ProcessId != 0 && n.ProcessId != report.ProcessId);
            if (external is not null)
            {
                insights.Add(
                    $"Thread {chain.ThreadId} is waiting on ANOTHER PROCESS " +
                    $"(pid {external.ProcessId}, thread {external.ThreadId}): {Describe(chain)}");
                continue;
            }

            var blocker = chain.Nodes.FirstOrDefault(n => n.Kind != WaitNodeKind.Thread);
            if (blocker is null)
            {
                continue;
            }

            if (blocker.Status == WaitNodeStatus.Abandoned)
            {
                insights.Add(
                    $"Thread {chain.ThreadId} waits on ABANDONED {Label(blocker.Kind)}" +
                    $"{NameSuffix(blocker)} — its owner died while holding it: {Describe(chain)}");
                continue;
            }

            string ownerSuffix = OwnerOf(chain, blocker) is { } owner
                ? $" held by thread {owner.ThreadId}"
                : string.Empty;

            insights.Add(blocker.Kind switch
            {
                WaitNodeKind.Mutex or WaitNodeKind.CriticalSection =>
                    $"Thread {chain.ThreadId} is blocked on a {Label(blocker.Kind)}{NameSuffix(blocker)}{ownerSuffix}: {Describe(chain)}",
                WaitNodeKind.SendMessage =>
                    $"Thread {chain.ThreadId} is stuck in SendMessage — waiting for another window to answer: {Describe(chain)}",
                WaitNodeKind.Alpc or WaitNodeKind.Com or WaitNodeKind.ComActivation =>
                    $"Thread {chain.ThreadId} is waiting on an RPC/COM call{ownerSuffix}: {Describe(chain)}",
                WaitNodeKind.SocketIo or WaitNodeKind.SmbIo =>
                    $"Thread {chain.ThreadId} is waiting on network/file-share I/O: {Describe(chain)}",
                WaitNodeKind.ProcessWait or WaitNodeKind.ThreadWait =>
                    $"Thread {chain.ThreadId} is waiting for a process or thread to exit: {Describe(chain)}",
                _ =>
                    $"Thread {chain.ThreadId} is blocked on {Label(blocker.Kind)}{NameSuffix(blocker)}: {Describe(chain)}",
            });
        }

        if (insights.Count == 0)
        {
            insights.Add(
                "No blocking wait chains detected — threads appear busy, sleeping, or waiting " +
                "on objects WCT cannot attribute (events, .NET locks).");
        }

        return insights;
    }

    /// <summary>"thread 123 → mutex 'Foo' [Owned] → thread 456"</summary>
    public static string Describe(ThreadWaitChain chain)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < chain.Nodes.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(" → ");
            }

            var node = chain.Nodes[i];
            if (node.Kind == WaitNodeKind.Thread)
            {
                sb.Append($"thread {node.ThreadId} [{node.Status}]");
            }
            else
            {
                sb.Append($"{Label(node.Kind)}{NameSuffix(node)} [{node.Status}]");
            }
        }

        return sb.ToString();
    }

    private static WaitChainNode? OwnerOf(ThreadWaitChain chain, WaitChainNode blocker)
    {
        int index = chain.Nodes.ToList().IndexOf(blocker);
        var next = index >= 0 && index + 1 < chain.Nodes.Count ? chain.Nodes[index + 1] : null;
        return next?.Kind == WaitNodeKind.Thread ? next : null;
    }

    private static string NameSuffix(WaitChainNode node) =>
        node.ObjectName is null ? string.Empty : $" '{node.ObjectName}'";

    private static string Label(WaitNodeKind kind) => kind switch
    {
        WaitNodeKind.CriticalSection => "critical section",
        WaitNodeKind.SendMessage => "SendMessage wait",
        WaitNodeKind.Mutex => "mutex",
        WaitNodeKind.Alpc => "ALPC call",
        WaitNodeKind.Com => "COM call",
        WaitNodeKind.ComActivation => "COM activation",
        WaitNodeKind.ThreadWait => "thread wait",
        WaitNodeKind.ProcessWait => "process wait",
        WaitNodeKind.SocketIo => "socket I/O",
        WaitNodeKind.SmbIo => "SMB I/O",
        WaitNodeKind.Thread => "thread",
        _ => "unknown object",
    };
}
