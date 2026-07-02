using NoKill.Automation;
using NoKill.Core.Models;
using NoKill.Diagnostics;

// nokill-cli: read-only window inventory + hidden-blocker detection.
// Usage: NoKill.Cli [--flagged-only] [--reveal]
//   --flagged-only  only list windows flagged as hung / likely hung
//   --reveal        raise any hidden modal blockers found (the one safe action)
bool flaggedOnly = args.Contains("--flagged-only");
bool reveal = args.Contains("--reveal");

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

static string Truncate(string value, int max) =>
    value.Length <= max ? value : value[..(max - 3)] + "...";
