using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.Diagnostics.ProcessSnapshotting;
using Windows.Win32.System.Threading;

namespace NoKill.Research;

/// <summary>
/// RESEARCH ONLY. Wraps PssCaptureSnapshot — Windows Process Snapshotting —
/// to capture a consistent, copy-on-write view of a process for inspection.
/// This is the honest floor of the "clone the process" branch: a snapshot is
/// a diagnostic terrarium, not a runnable resurrection. Never wired into the
/// stable rescue path; reachable only through the CLI's research opt-in.
/// </summary>
public sealed class ProcessSnapshotService
{
    // CONTEXT flags required when capturing thread context (CONTEXT_ALL-ish).
    private const uint ThreadContextFlags = 0x0010000B;

    public ProcessSnapshotReport Capture(int pid)
    {
        SafeHandle? process = null;
        try
        {
            // PROCESS_CREATE_PROCESS is what the VA clone needs (it uses process
            // reflection, which spawns a clone process from the target).
            process = PInvoke.OpenProcess_SafeHandle(
                PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_INFORMATION
                    | PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ
                    | PROCESS_ACCESS_RIGHTS.PROCESS_VM_OPERATION
                    | PROCESS_ACCESS_RIGHTS.PROCESS_DUP_HANDLE
                    | PROCESS_ACCESS_RIGHTS.PROCESS_CREATE_PROCESS,
                bInheritHandle: false,
                (uint)pid);

            if (process.IsInvalid)
            {
                return ProcessSnapshotReport.Failed(
                    pid, $"Cannot open process {pid} (error {Marshal.GetLastWin32Error()}); elevation may be required.");
            }

            const PSS_CAPTURE_FLAGS baseFlags =
                PSS_CAPTURE_FLAGS.PSS_CAPTURE_HANDLES
                | PSS_CAPTURE_FLAGS.PSS_CAPTURE_HANDLE_BASIC_INFORMATION
                | PSS_CAPTURE_FLAGS.PSS_CAPTURE_THREADS
                | PSS_CAPTURE_FLAGS.PSS_CAPTURE_THREAD_CONTEXT;

            // Try with the VA clone; if that specific privilege is denied, fall
            // back to a clone-less snapshot so inspection still succeeds.
            uint result = PInvoke.PssCaptureSnapshot(
                process, baseFlags | PSS_CAPTURE_FLAGS.PSS_CAPTURE_VA_CLONE, ThreadContextFlags, out HPSS snapshot);

            if (result != 0)
            {
                result = PInvoke.PssCaptureSnapshot(process, baseFlags, ThreadContextFlags, out snapshot);
            }

            if (result != 0)
            {
                return ProcessSnapshotReport.Failed(pid, $"PssCaptureSnapshot failed with error {result}.");
            }

            try
            {
                return Query(pid, snapshot);
            }
            finally
            {
                PInvoke.PssFreeSnapshot(process, snapshot);
            }
        }
        catch (Exception ex)
        {
            return ProcessSnapshotReport.Failed(pid, ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static ProcessSnapshotReport Query(int pid, HPSS snapshot)
    {
        return new ProcessSnapshotReport
        {
            ProcessId = pid,
            Captured = true,
            ThreadCount = QueryCapturedCount(snapshot, PSS_QUERY_INFORMATION_CLASS.PSS_QUERY_THREAD_INFORMATION),
            HandleCount = QueryCapturedCount(snapshot, PSS_QUERY_INFORMATION_CLASS.PSS_QUERY_HANDLE_INFORMATION),
            VaCloneCreated = QueryVaClone(snapshot),
            CapturedAt = DateTimeOffset.Now,
        };
    }

    /// <summary>
    /// PSS_THREAD_INFORMATION and PSS_HANDLE_INFORMATION are each two DWORDs
    /// (8 bytes) that begin with the captured count. PssQuerySnapshot requires
    /// the buffer length to match the struct size exactly, so the span is 8.
    /// </summary>
    private static int QueryCapturedCount(HPSS snapshot, PSS_QUERY_INFORMATION_CLASS infoClass)
    {
        Span<byte> buffer = stackalloc byte[8];
        uint status = PInvoke.PssQuerySnapshot(snapshot, infoClass, buffer);
        return status == 0 ? BitConverter.ToInt32(buffer) : 0;
    }

    /// <summary>
    /// PSS_VA_CLONE_INFORMATION starts with the clone process HANDLE; non-null
    /// means a copy-on-write clone of the address space was created.
    /// </summary>
    private static bool QueryVaClone(HPSS snapshot)
    {
        Span<byte> buffer = stackalloc byte[IntPtr.Size];
        uint status = PInvoke.PssQuerySnapshot(
            snapshot, PSS_QUERY_INFORMATION_CLASS.PSS_QUERY_VA_CLONE_INFORMATION, buffer);
        nint cloneHandle = MemoryMarshal.Read<nint>(buffer);
        return status == 0 && cloneHandle != 0;
    }
}
