using NoKill.Automation;
using NoKill.Core.Models;
using NoKill.Diagnostics;
using NoKill.Profiles;
using NoKill.Vault;
using NoKill.Win32;

// nokill-cli: read-only window inventory + hidden-blocker detection + vault preserve.
// Usage: NoKill.Cli [--flagged-only] [--reveal] [--preserve <pid>]
//   --flagged-only    only list windows flagged as hung / likely hung
//   --reveal          raise any hidden modal blockers found (the one safe action)
//   --preserve <pid>  preserve rescue evidence for a process into the Recovery Vault
bool flaggedOnly = args.Contains("--flagged-only");
bool reveal = args.Contains("--reveal");

int preserveArgIndex = Array.IndexOf(args, "--preserve");
if (preserveArgIndex >= 0)
{
    if (preserveArgIndex + 1 >= args.Length || !int.TryParse(args[preserveArgIndex + 1], out int targetPid))
    {
        Console.Error.WriteLine("Usage: NoKill.Cli --preserve <pid>");
        return 2;
    }

    return PreserveToVault(targetPid);
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

static int PreserveToVault(int pid)
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

    var vault = new RecoveryVault();
    var result = vault.Preserve(new VaultEntryRequest
    {
        TargetWindow = target,
        ProcessInfo = processInfo,
        ProcessWindows = processWindows,
        Blockers = blockers,
        ScreenshotPng = target is not null ? WindowCapture.TryCapturePng(target.WindowHandle) : null,
        Artifacts = plan.Artifacts,
        AppliedProfiles = plan.AppliedProfiles,
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
