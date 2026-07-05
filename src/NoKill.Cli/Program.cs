using NoKill.Automation;
using NoKill.Core.Models;
using NoKill.Diagnostics;
using NoKill.Profiles;
using NoKill.Vault;
using NoKill.Win32;

// nokill-cli: read-only window inventory + hidden-blocker detection + vault preserve.
// Usage: NoKill.Cli [--flagged-only] [--reveal] [--preserve <pid>] [--waitchain <pid>]
//   --flagged-only    only list windows flagged as hung / likely hung
//   --reveal          raise any hidden modal blockers found (the one safe action)
//   --preserve <pid>  preserve rescue evidence for a process into the Recovery Vault
//   --dump <level>    with --preserve: minidump level "triage" (default), "full", or "none"
//   --waitchain <pid> analyze why a process's threads are blocked (deadlocks, waits)
bool flaggedOnly = args.Contains("--flagged-only");
bool reveal = args.Contains("--reveal");

int preserveArgIndex = Array.IndexOf(args, "--preserve");
if (preserveArgIndex >= 0)
{
    if (preserveArgIndex + 1 >= args.Length || !int.TryParse(args[preserveArgIndex + 1], out int targetPid))
    {
        Console.Error.WriteLine("Usage: NoKill.Cli --preserve <pid> [--dump triage|full|none]");
        return 2;
    }

    int dumpArgIndex = Array.IndexOf(args, "--dump");
    string dumpLevel = dumpArgIndex >= 0 && dumpArgIndex + 1 < args.Length
        ? args[dumpArgIndex + 1].ToLowerInvariant()
        : "triage";
    if (dumpLevel is not ("triage" or "full" or "none"))
    {
        Console.Error.WriteLine($"Unknown dump level '{dumpLevel}'. Use triage, full, or none.");
        return 2;
    }

    return PreserveToVault(targetPid, dumpLevel);
}

int waitChainArgIndex = Array.IndexOf(args, "--waitchain");
if (waitChainArgIndex >= 0)
{
    if (waitChainArgIndex + 1 >= args.Length || !int.TryParse(args[waitChainArgIndex + 1], out int wcPid))
    {
        Console.Error.WriteLine("Usage: NoKill.Cli --waitchain <pid>");
        return 2;
    }

    return AnalyzeWaitChains(wcPid);
}

var inventory = new WindowInventoryService();
var windows = inventory.Snapshot()
    .OrderByDescending(w => w.Status)
    .ThenBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
    .ToList();

if (flaggedOnly)
{
    windows = windows.Where(w => w.Status is HangStatus.LikelyHung or HangStatus.NotResponding).ToList();
}

Console.WriteLine($"{"PID",7}  {"STATUS",-14}  {"PROCESS",-24}  TITLE");
foreach (var w in windows)
{
    Console.WriteLine($"{w.ProcessId,7}  {w.Status,-14}  {Truncate(w.ProcessName, 24),-24}  {Truncate(w.Title, 60)}");
}

int flagged = windows.Count(w => w.Status is HangStatus.LikelyHung or HangStatus.NotResponding);
Console.WriteLine();
Console.WriteLine($"{windows.Count} window(s) listed, {flagged} flagged as hung or likely hung.");

var detector = new HiddenDialogDetector();
var findings = detector.Detect();

if (findings.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("SUSPECTED MODAL BLOCKERS");
    foreach (var f in findings)
    {
        string visibility = f.BlockerIsNotOnScreen
            ? "OFF-SCREEN"
            : f.BlockerIsBehindBlockedWindow ? "HIDDEN BEHIND OWNER" : "visible";

        Console.WriteLine($"  \"{f.BlockedWindowTitle}\" ({f.ProcessName}, pid {f.ProcessId})");
        Console.WriteLine($"    blocked by: \"{f.BlockerTitle}\"  [{visibility}]");
        if (f.BlockerContent is not null)
        {
            Console.WriteLine($"    dialog says: {f.BlockerContent}");
        }

        if (reveal && f.IsHiddenBlocker)
        {
            bool ok = HiddenDialogDetector.Reveal(f);
            Console.WriteLine($"    reveal: {(ok ? "raised blocker to front" : "FAILED")}");
        }
    }
}
else
{
    Console.WriteLine("No modal blockers detected.");
}

return 0;

static int AnalyzeWaitChains(int pid)
{
    var report = new WaitChainAnalyzer().Analyze(pid);
    if (report is null)
    {
        Console.Error.WriteLine("Wait Chain Traversal is unavailable on this system.");
        return 1;
    }

    Console.WriteLine($"Wait-chain analysis for pid {pid} ({report.Chains.Count} thread(s) walked):");
    Console.WriteLine();

    foreach (var chain in report.Chains)
    {
        string cycleMark = chain.IsCycle ? "  [DEADLOCK CYCLE]" : string.Empty;
        string marker = chain.IsBlockedOnSomething || chain.IsCycle ? "*" : " ";
        Console.WriteLine($" {marker} {WaitChainInterpreter.Describe(chain)}{cycleMark}");
    }

    Console.WriteLine();

    Console.WriteLine("Insights:");
    foreach (string insight in WaitChainInterpreter.Interpret(report))
    {
        Console.WriteLine($"  - {insight}");
    }

    foreach (string error in report.Errors)
    {
        Console.WriteLine($"  note: {error}");
    }

    return report.DeadlockDetected ? 3 : 0;
}

static int PreserveToVault(int pid, string dumpLevel)
{
    var processInfo = ProcessInspector.TryInspect(pid);
    var inventory = new WindowInventoryService();
    var processWindows = inventory.Snapshot().Where(w => w.ProcessId == pid).ToList();

    // Windowless processes and services are first-class citizens: no window
    // just means no screenshot and no hang status — evidence still gets vaulted.
    var target = processWindows.OrderByDescending(w => w.Status).FirstOrDefault();

    if (target is null && processInfo is null)
    {
        Console.Error.WriteLine($"No process with pid {pid} found (or access was denied).");
        return 1;
    }

    string processName = target?.ProcessName ?? processInfo!.ProcessName;
    string? exePath = target?.ExecutablePath ?? processInfo?.ExecutablePath;

    var blockers = target is not null
        ? new HiddenDialogDetector().Detect().Where(f => f.ProcessId == pid).ToList()
        : [];

    var plan = new ArtifactPlanner().PlanFor(processName, exePath);
    var waitChains = new WaitChainAnalyzer().Analyze(pid);

    var vault = new RecoveryVault();

    string? dumpTempPath = null;
    if (dumpLevel != "none")
    {
        var detail = dumpLevel == "full" ? DumpDetail.Full : DumpDetail.Triage;
        string stagePath = vault.CreateTempFilePath(".dmp");
        var (dumpOk, dumpError) = MiniDumpWriter.TryWrite(pid, stagePath, detail);
        if (dumpOk)
        {
            dumpTempPath = stagePath;
        }
        else
        {
            Console.Error.WriteLine($"Minidump capture failed: {dumpError}");
        }
    }

    var result = vault.Preserve(new VaultEntryRequest
    {
        TargetWindow = target,
        ProcessInfo = processInfo,
        ProcessWindows = processWindows,
        Blockers = blockers,
        ScreenshotPng = target is not null ? WindowCapture.TryCapturePng(target.WindowHandle) : null,
        Artifacts = plan.Artifacts,
        AppliedProfiles = plan.AppliedProfiles,
        WaitChains = waitChains,
        WaitChainInsights = waitChains is not null ? WaitChainInterpreter.Interpret(waitChains) : [],
        MinidumpTempPath = dumpTempPath,
        MinidumpDetail = dumpTempPath is not null ? dumpLevel : null,
        Reason = "manual preserve via CLI",
    });

    Console.WriteLine($"Vault entry: {result.EntryDirectory}");
    Console.WriteLine($"Profiles applied: {string.Join(", ", plan.AppliedProfiles)}");
    foreach (string warning in plan.Warnings)
    {
        Console.WriteLine($"  profile warning: {warning}");
    }
    foreach (string file in result.SavedFiles)
    {
        Console.WriteLine($"  saved: {Path.GetRelativePath(result.EntryDirectory, file)}");
    }

    foreach (string warning in result.Warnings)
    {
        Console.WriteLine($"  warning: {warning}");
    }

    return 0;
}

static string Truncate(string value, int max) =>
    value.Length <= max ? value : value[..(max - 3)] + "...";
