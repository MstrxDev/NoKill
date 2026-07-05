namespace NoKill.Core.Models;

/// <summary>What kind of thing a wait-chain node is.</summary>
public enum WaitNodeKind
{
    Unknown,
    Thread,
    CriticalSection,
    SendMessage,
    Mutex,
    Alpc,
    Com,
    ThreadWait,
    ProcessWait,
    ComActivation,
    SocketIo,
    SmbIo,
}

/// <summary>State of a wait-chain node.</summary>
public enum WaitNodeStatus
{
    Unknown,
    NoAccess,
    Running,
    Blocked,
    PidOnly,
    PidOnlyRpcss,
    Owned,
    NotOwned,
    Abandoned,
    Error,
}

/// <summary>One node in a thread's wait chain: either a thread or the synchronization object it waits on.</summary>
public sealed record WaitChainNode
{
    public required WaitNodeKind Kind { get; init; }

    public required WaitNodeStatus Status { get; init; }

    /// <summary>Set for thread nodes; 0 for object nodes.</summary>
    public int ProcessId { get; init; }

    public int ThreadId { get; init; }

    public long WaitTimeMs { get; init; }

    /// <summary>Object name when the kernel knows one (named mutexes etc.).</summary>
    public string? ObjectName { get; init; }
}

/// <summary>The full wait chain for one thread, walked from the thread outward.</summary>
public sealed record ThreadWaitChain
{
    public required int ThreadId { get; init; }

    /// <summary>True when the chain loops back on itself — a genuine deadlock.</summary>
    public required bool IsCycle { get; init; }

    public required IReadOnlyList<WaitChainNode> Nodes { get; init; }

    /// <summary>A chain of just the thread itself means nothing blocks it (that WCT can see).</summary>
    public bool IsBlockedOnSomething => Nodes.Count > 1;
}

/// <summary>Wait-chain analysis for a whole process at one point in time.</summary>
public sealed record WaitChainReport
{
    public required int ProcessId { get; init; }

    public required IReadOnlyList<ThreadWaitChain> Chains { get; init; }

    /// <summary>Threads whose chains could not be walked (access denied etc.), with the reason.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    public required DateTimeOffset CapturedAt { get; init; }

    public bool DeadlockDetected => Chains.Any(c => c.IsCycle);
}
