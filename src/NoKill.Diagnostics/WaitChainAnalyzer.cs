using NoKill.Core.Models;
using NoKill.Win32;

namespace NoKill.Diagnostics;

/// <summary>
/// Walks the wait chains of every thread in a process to answer the question
/// hang detection can't: WHY is it stuck. Read-only, like everything else in
/// the diagnosis path.
/// </summary>
public sealed class WaitChainAnalyzer
{
    private const int MaxThreads = 64;

    /// <summary>Analyzes all threads of a process, or null when WCT is unavailable.</summary>
    public WaitChainReport? Analyze(int processId)
    {
        using var session = WaitChainSession.TryOpen();
        if (session is null)
        {
            return null;
        }

        var chains = new List<ThreadWaitChain>();
        var errors = new List<string>();

        var threadIds = ThreadEnumerator.GetThreadIds((uint)processId);
        foreach (uint threadId in threadIds.Take(MaxThreads))
        {
            var chain = session.TryGetChain(threadId);
            if (chain is null)
            {
                errors.Add($"Thread {threadId}: wait chain unavailable (exited or access denied).");
            }
            else
            {
                chains.Add(chain);
            }
        }

        if (threadIds.Count > MaxThreads)
        {
            errors.Add($"Process has {threadIds.Count} threads; only the first {MaxThreads} were analyzed.");
        }

        return new WaitChainReport
        {
            ProcessId = processId,
            Chains = chains,
            Errors = errors,
            CapturedAt = DateTimeOffset.Now,
        };
    }
}
