using NoKill.Core.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.Debug;

namespace NoKill.Win32;

/// <summary>
/// Wraps Windows Wait Chain Traversal (wct.h) — the OS facility built
/// specifically for diagnosing hangs and deadlocks. Read-only: WCT inspects
/// wait relationships; it cannot and does not alter the target.
/// </summary>
public sealed class WaitChainSession : IDisposable
{
    private readonly nint _handle;
    private bool _disposed;

    private WaitChainSession(nint handle)
    {
        _handle = handle;
    }

    /// <summary>Opens a synchronous WCT session, or null when WCT is unavailable.</summary>
    public static unsafe WaitChainSession? TryOpen()
    {
        void* handle = PInvoke.OpenThreadWaitChainSession(0, null);
        return handle is null ? null : new WaitChainSession((nint)handle);
    }

    /// <summary>
    /// Walks one thread's wait chain. Returns null when the walk fails
    /// (thread exited, access denied).
    /// </summary>
    public unsafe ThreadWaitChain? TryGetChain(uint threadId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Span<WAITCHAIN_NODE_INFO> nodes = stackalloc WAITCHAIN_NODE_INFO[(int)PInvoke.WCT_MAX_NODE_COUNT];
        uint count = PInvoke.WCT_MAX_NODE_COUNT;

        if (!PInvoke.GetThreadWaitChain(
            (void*)_handle,
            Context: 0,
            WAIT_CHAIN_THREAD_OPTIONS.WCT_OUT_OF_PROC_FLAG
                | WAIT_CHAIN_THREAD_OPTIONS.WCT_OUT_OF_PROC_COM_FLAG
                | WAIT_CHAIN_THREAD_OPTIONS.WCT_OUT_OF_PROC_CS_FLAG,
            threadId,
            ref count,
            nodes,
            out BOOL isCycle))
        {
            return null;
        }

        var mapped = new List<WaitChainNode>((int)count);
        for (int i = 0; i < count; i++)
        {
            mapped.Add(Map(in nodes[i]));
        }

        return new ThreadWaitChain
        {
            ThreadId = (int)threadId,
            IsCycle = isCycle,
            Nodes = mapped,
        };
    }

    private static WaitChainNode Map(in WAITCHAIN_NODE_INFO node)
    {
        var kind = node.ObjectType switch
        {
            WCT_OBJECT_TYPE.WctThreadType => WaitNodeKind.Thread,
            WCT_OBJECT_TYPE.WctCriticalSectionType => WaitNodeKind.CriticalSection,
            WCT_OBJECT_TYPE.WctSendMessageType => WaitNodeKind.SendMessage,
            WCT_OBJECT_TYPE.WctMutexType => WaitNodeKind.Mutex,
            WCT_OBJECT_TYPE.WctAlpcType => WaitNodeKind.Alpc,
            WCT_OBJECT_TYPE.WctComType => WaitNodeKind.Com,
            WCT_OBJECT_TYPE.WctThreadWaitType => WaitNodeKind.ThreadWait,
            WCT_OBJECT_TYPE.WctProcessWaitType => WaitNodeKind.ProcessWait,
            WCT_OBJECT_TYPE.WctComActivationType => WaitNodeKind.ComActivation,
            WCT_OBJECT_TYPE.WctSocketIoType => WaitNodeKind.SocketIo,
            WCT_OBJECT_TYPE.WctSmbIoType => WaitNodeKind.SmbIo,
            _ => WaitNodeKind.Unknown,
        };

        var status = node.ObjectStatus switch
        {
            WCT_OBJECT_STATUS.WctStatusNoAccess => WaitNodeStatus.NoAccess,
            WCT_OBJECT_STATUS.WctStatusRunning => WaitNodeStatus.Running,
            WCT_OBJECT_STATUS.WctStatusBlocked => WaitNodeStatus.Blocked,
            WCT_OBJECT_STATUS.WctStatusPidOnly => WaitNodeStatus.PidOnly,
            WCT_OBJECT_STATUS.WctStatusPidOnlyRpcss => WaitNodeStatus.PidOnlyRpcss,
            WCT_OBJECT_STATUS.WctStatusOwned => WaitNodeStatus.Owned,
            WCT_OBJECT_STATUS.WctStatusNotOwned => WaitNodeStatus.NotOwned,
            WCT_OBJECT_STATUS.WctStatusAbandoned => WaitNodeStatus.Abandoned,
            WCT_OBJECT_STATUS.WctStatusError => WaitNodeStatus.Error,
            _ => WaitNodeStatus.Unknown,
        };

        if (kind == WaitNodeKind.Thread)
        {
            return new WaitChainNode
            {
                Kind = kind,
                Status = status,
                ProcessId = (int)node.Anonymous.ThreadObject.ProcessId,
                ThreadId = (int)node.Anonymous.ThreadObject.ThreadId,
                WaitTimeMs = node.Anonymous.ThreadObject.WaitTime,
            };
        }

        string name = node.Anonymous.LockObject.ObjectName.ToString();
        return new WaitChainNode
        {
            Kind = kind,
            Status = status,
            ObjectName = name.Length > 0 ? name : null,
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            unsafe
            {
                PInvoke.CloseThreadWaitChainSession((void*)_handle);
            }
        }
    }
}
