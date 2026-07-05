using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace NoKill.Win32;

/// <summary>How much of the target process a dump should contain.</summary>
public enum DumpDetail
{
    /// <summary>
    /// Thread stacks, thread/handle info, unloaded modules. Small (tens of MB)
    /// and enough to see where every thread is stuck. The default.
    /// </summary>
    Triage,

    /// <summary>
    /// Full process memory. Can be gigabytes; needed for deep post-mortem
    /// (managed heap inspection, data recovery research). Opt-in only.
    /// </summary>
    Full,
}

/// <summary>
/// Captures a minidump of another process via dbghelp!MiniDumpWriteDump.
/// Read-only observation: the target is briefly suspended while the dump is
/// written (desirable — it yields a consistent snapshot of an already-frozen
/// app) and resumes untouched.
/// </summary>
public static partial class MiniDumpWriter
{
    // Hand-written interop: CsWin32 refuses to generate MiniDumpWriteDump for
    // AnyCPU (PInvoke005 — the MINIDUMP_* structures are arch-specific). We
    // pass null for all three structure parameters, so the signature below is
    // identical on every architecture.
    [LibraryImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MiniDumpWriteDump(
        SafeHandle hProcess,
        uint processId,
        SafeFileHandle hFile,
        uint dumpType,
        nint exceptionParam,
        nint userStreamParam,
        nint callbackParam);

    // MINIDUMP_TYPE flags we use (minidumpapiset.h; stable, documented values)
    private const uint MiniDumpNormal = 0x00000000;
    private const uint MiniDumpWithFullMemory = 0x00000002;
    private const uint MiniDumpWithHandleData = 0x00000004;
    private const uint MiniDumpWithUnloadedModules = 0x00000020;
    private const uint MiniDumpWithProcessThreadData = 0x00000100;
    private const uint MiniDumpWithThreadInfo = 0x00001000;

    // HRESULT for ERROR_PARTIAL_COPY: the target's memory map changed mid-dump
    // (process still initializing, or actively allocating). Transient — retry.
    private const int PartialCopyHResult = unchecked((int)0x8007012B);

    public static (bool Success, string? Error) TryWrite(int pid, string outputPath, DumpDetail detail)
    {
        (bool Success, string? Error, int Code) result = (false, "not attempted", 0);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
            {
                Thread.Sleep(500);
            }

            result = TryWriteOnce(pid, outputPath, detail);
            if (result.Success || result.Code != PartialCopyHResult)
            {
                break;
            }
        }

        return (result.Success, result.Error);
    }

    private static (bool Success, string? Error, int Code) TryWriteOnce(int pid, string outputPath, DumpDetail detail)
    {
        try
        {
            using SafeFileHandle process = PInvoke.OpenProcess_SafeHandle(
                PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_INFORMATION
                    | PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ
                    | PROCESS_ACCESS_RIGHTS.PROCESS_DUP_HANDLE,
                bInheritHandle: false,
                (uint)pid);

            if (process.IsInvalid)
            {
                int openError = Marshal.GetLastWin32Error();
                return (false, $"Cannot open process {pid} (error {openError}); elevation may be required.", openError);
            }

            uint dumpType = detail switch
            {
                DumpDetail.Full =>
                    MiniDumpWithFullMemory | MiniDumpWithHandleData
                        | MiniDumpWithThreadInfo | MiniDumpWithUnloadedModules,
                _ =>
                    MiniDumpNormal | MiniDumpWithThreadInfo | MiniDumpWithHandleData
                        | MiniDumpWithUnloadedModules | MiniDumpWithProcessThreadData,
            };

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using (var file = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                if (MiniDumpWriteDump(process, (uint)pid, file.SafeFileHandle, dumpType, 0, 0, 0))
                {
                    return (true, null, 0);
                }
            }

            // MiniDumpWriteDump reports failures as HRESULTs through last-error.
            int error = Marshal.GetLastWin32Error();
            TryDeletePartialFile(outputPath);
            return (false, $"MiniDumpWriteDump failed with 0x{error:X8}.", error);
        }
        catch (Exception ex)
        {
            TryDeletePartialFile(outputPath);
            return (false, ex.Message, 0);
        }
    }

    private static void TryDeletePartialFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // a stray partial file is untidy but harmless
        }
    }
}
