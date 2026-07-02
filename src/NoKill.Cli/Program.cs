using NoKill.Core.Models;
using NoKill.Diagnostics;

// nokill-cli: read-only window inventory for testing and scripting.
// Usage: NoKill.Cli [--flagged-only]
bool flaggedOnly = args.Contains("--flagged-only");

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
    string title = w.Title.Length > 60 ? w.Title[..57] + "..." : w.Title;
    Console.WriteLine($"{w.ProcessId,7}  {w.Status,-14}  {Truncate(w.ProcessName, 24),-24}  {title}");
}

Console.WriteLine();
int flagged = windows.Count(w => w.Status is HangStatus.LikelyHung or HangStatus.NotResponding);
Console.WriteLine($"{windows.Count} window(s) listed, {flagged} flagged as hung or likely hung.");

static string Truncate(string value, int max) =>
    value.Length <= max ? value : value[..(max - 3)] + "...";
