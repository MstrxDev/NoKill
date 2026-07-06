using NoKill.Automation;
using NoKill.Core.Models;
using NoKill.Diagnostics;
using NoKill.Profiles;
using NoKill.Research;
using NoKill.Sdk;
using NoKill.Vault;
using NoKill.Win32;

// nokill-cli: read-only window inventory + hidden-blocker detection + vault preserve.
// Usage: NoKill.Cli [--flagged-only] [--reveal] [--preserve <pid>] [--waitchain <pid>]
//   --flagged-only    only list windows flagged as hung / likely hung
//   --reveal          raise any hidden modal blockers found (the one safe action)
//   --preserve <pid>  preserve rescue evidence for a process into the Recovery Vault
//   --dump <level>    with --preserve: minidump level "triage" (default), "full", or "none"
//   --waitchain <pid> analyze why a process's threads are blocked (deadlocks, waits)
//   --watch           run as a background watchdog: auto-preserve evidence when
//                     any app freezes (options: --dump, --poll-seconds, --confirm-seconds)
//   --history [n]     show the last n freeze incidents (default 20) and top offenders
//   --snapshot <pid>  RESEARCH: capture a Pss diagnostic snapshot (requires --research)
//   --research        opt in to experimental research-branch features
bool flaggedOnly = args.Contains("--flagged-only");
bool reveal = args.Contains("--reveal");
bool researchEnabled = args.Contains("--research");

int snapshotArgIndex = Array.IndexOf(args, "--snapshot");
if (snapshotArgIndex >= 0)
{
    if (!researchEnabled)
    {
        Console.Error.WriteLine(
            "--snapshot is an experimental research feature. Re-run with --research to opt in.");
        return 2;
    }

    if (snapshotArgIndex + 1 >= args.Length || !int.TryParse(args[snapshotArgIndex + 1], out int snapPid))
    {
        Console.Error.WriteLine("Usage: NoKill.Cli --snapshot <pid> --research");
        return 2;
    }

    return RunSnapshot(snapPid);
}

if (args.Contains("--history"))
{
    int historyCount = int.TryParse(ReadOption(args, "--history"), out int h) ? h : 20;
    return ShowHistory(historyCount);
}

if (args.Contains("--watch"))
{
    string watchDumpLevel = ReadOption(args, "--dump") ?? "triage";
    if (watchDumpLevel is not ("triage" or "full" or "none"))
    {
        Console.Error.WriteLine($"Unknown dump level '{watchDumpLevel}'. Use triage, full, or none.");
        return 2;
    }

    int pollSeconds = int.TryParse(ReadOption(args, "--poll-seconds"), out int p) ? p : 3;
    int confirmSeconds = int.TryParse(ReadOption(args, "--confirm-seconds"), out int c) ? c : 10;

    return await RunWatchAsync(watchDumpLevel, pollSeconds, confirmSeconds);
}

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

    return PreserveToVault(targetPid, dumpLevel, "manual").Code;
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

static async Task<int> RunWatchAsync(string dumpLevel, int pollSeconds, int confirmSeconds)
{
    var watchdog = new Watchdog(new WatchdogOptions
    {
        PollInterval = TimeSpan.FromSeconds(pollSeconds),
        ConfirmAfter = TimeSpan.FromSeconds(confirmSeconds),
    });

    var activeIncidents = new Dictionary<int, long>();

    watchdog.FreezeDetected += incident =>
    {
        WatchLog($"FREEZE: {incident.ProcessName} (pid {incident.ProcessId}) " +
                 $"not responding for {incident.HungFor.TotalSeconds:F0}s — preserving evidence…");
        try
        {
            var (_, incidentId) = PreserveToVault(incident.ProcessId, dumpLevel, "watchdog");
            if (incidentId > 0)
            {
                activeIncidents[incident.ProcessId] = incidentId;
            }
        }
        catch (Exception ex)
        {
            WatchLog($"preserve failed: {ex.Message}");
        }
    };

    watchdog.FreezeEnded += incident =>
    {
        WatchLog($"ENDED: {incident.ProcessName} (pid {incident.ProcessId}) recovered or exited " +
                 $"after {incident.HungFor.TotalSeconds:F0}s");
        if (activeIncidents.Remove(incident.ProcessId, out long incidentId))
        {
            try
            {
                new FreezeHistory().MarkEnded(incidentId);
            }
            catch (Exception ex)
            {
                WatchLog($"history update failed: {ex.Message}");
            }
        }
    };

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    WatchLog($"Watchdog running: scanning every {pollSeconds}s, " +
             $"confirming freezes after {confirmSeconds}s, dump level '{dumpLevel}'. Ctrl+C to stop.");
    await watchdog.RunAsync(cts.Token);
    WatchLog("Watchdog stopped.");
    return 0;

    static void WatchLog(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
}

static string? ReadOption(string[] args, string name)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

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

static int RunSnapshot(int pid)
{
    Console.WriteLine("== RESEARCH: process snapshot (diagnostic only, not a runnable clone) ==");

    var report = new ProcessSnapshotService().Capture(pid);
    if (!report.Captured)
    {
        Console.Error.WriteLine($"Snapshot failed: {report.Error}");
        return 1;
    }

    Console.WriteLine($"Captured snapshot of pid {pid} at {report.CapturedAt:HH:mm:ss}:");
    Console.WriteLine($"  VA clone created: {report.VaCloneCreated}");
    Console.WriteLine($"  Threads captured: {report.ThreadCount}");
    Console.WriteLine($"  Handles captured: {report.HandleCount}");

    // Cooperative recovery: surface any checkpoints the app wrote via the SDK.
    var processName = TryGetProcessName(pid);
    if (processName is not null)
    {
        var checkpoints = new CooperativeCheckpointReader().GetCheckpoints(processName);
        Console.WriteLine();
        if (checkpoints.Count > 0)
        {
            Console.WriteLine($"Cooperative recovery checkpoints for '{processName}':");
            foreach (var checkpoint in checkpoints)
            {
                Console.WriteLine(
                    $"  {checkpoint.LastWriteUtc.ToLocalTime():HH:mm:ss}  " +
                    $"{checkpoint.SizeBytes,8} B  {Path.GetFileName(checkpoint.Path)}");
            }
        }
        else
        {
            Console.WriteLine($"No cooperative recovery checkpoints found for '{processName}' " +
                              "(the app has not integrated NoKill.Sdk).");
        }
    }

    return 0;
}

static string? TryGetProcessName(int pid)
{
    try
    {
        using var process = System.Diagnostics.Process.GetProcessById(pid);
        return process.ProcessName;
    }
    catch
    {
        return null;
    }
}

static int ShowHistory(int count)
{
    var history = new FreezeHistory();
    var recent = history.GetRecent(count);

    if (recent.Count == 0)
    {
        Console.WriteLine("No freeze incidents recorded yet.");
        return 0;
    }

    Console.WriteLine($"Last {recent.Count} freeze incident(s):");
    Console.WriteLine($"{"WHEN",-17}  {"PROCESS",-20}  {"TRIGGER",-8}  {"DURATION",-9}  INSIGHT");
    foreach (var record in recent)
    {
        string duration = record.EndedAt is { } ended
            ? $"{(ended - record.StartedAt).TotalSeconds:F0}s"
            : "?";
        string insight = record.Insight ?? (record.VaultEntryPath is null ? "" : "evidence preserved");
        if (insight.Length > 70)
        {
            insight = insight[..67] + "...";
        }

        Console.WriteLine(
            $"{record.StartedAt:MM-dd HH:mm:ss}    {Truncate(record.ProcessName, 20),-20}  " +
            $"{record.Trigger,-8}  {duration,-9}  {insight}");
    }

    var offenders = history.GetTopOffenders(5);
    if (offenders.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Top offenders:");
        foreach (var offender in offenders)
        {
            Console.WriteLine(
                $"  {offender.ProcessName}: {offender.IncidentCount} incident(s), " +
                $"last {offender.LastIncidentAt:yyyy-MM-dd HH:mm}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"History database: {history.DatabasePath}");
    return 0;
}

static (int Code, long IncidentId) PreserveToVault(int pid, string dumpLevel, string trigger)
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
        return (1, 0);
    }

    string processName = target?.ProcessName ?? processInfo!.ProcessName;
    string? exePath = target?.ExecutablePath ?? processInfo?.ExecutablePath;

    var blockers = target is not null
        ? new HiddenDialogDetector().Detect().Where(f => f.ProcessId == pid).ToList()
        : [];

    var plan = new ArtifactPlanner().PlanFor(processName, exePath);
    var waitChains = new WaitChainAnalyzer().Analyze(pid);
    IReadOnlyList<string> insights = waitChains is not null ? WaitChainInterpreter.Interpret(waitChains) : [];

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
        WaitChainInsights = insights,
        MinidumpTempPath = dumpTempPath,
        MinidumpDetail = dumpTempPath is not null ? dumpLevel : null,
        Reason = $"{trigger} preserve via CLI",
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

    foreach (string prunedEntry in result.PrunedEntries)
    {
        Console.WriteLine($"  retention: pruned old entry {Path.GetFileName(prunedEntry)}");
    }

    long incidentId = 0;
    try
    {
        incidentId = new FreezeHistory().RecordIncident(
            processName, pid, exePath, trigger, result.EntryDirectory, insights.FirstOrDefault());
        Console.WriteLine($"History: incident #{incidentId} recorded.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"History recording failed: {ex.Message}");
    }

    return (0, incidentId);
}

static string Truncate(string value, int max) =>
    value.Length <= max ? value : value[..(max - 3)] + "...";
