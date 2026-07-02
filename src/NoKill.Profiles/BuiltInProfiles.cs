namespace NoKill.Profiles;

/// <summary>
/// Profiles shipped with NoKill. The universal profile carries the "works for
/// anything" heuristics; app-specific profiles add precision on top. All of
/// this is data — corrections and additions never require engine changes.
/// </summary>
public static class BuiltInProfiles
{
    /// <summary>
    /// Applies to EVERY process, window or not: crash dumps, per-app data
    /// folders matching the process name, logs beside the executable
    /// (services often log there), temp files, and classic autosave/backup
    /// filename patterns. Conservative caps keep it from flooding the vault.
    /// </summary>
    public static RescueProfile Universal { get; } = new()
    {
        Id = "universal",
        DisplayName = "Universal heuristics",
        ProcessNames = [],
        ArtifactRules =
        [
            new() { Path = @"%LOCALAPPDATA%\CrashDumps", Category = "crash-dumps", FilePattern = "{ProcessName}*.dmp", MaxAgeDays = 30, MaxFiles = 5 },
            new() { Path = @"%APPDATA%\{ProcessName}", Category = "logs", FilePattern = "*.log", Recursive = true, MaxAgeDays = 7, MaxFiles = 50 },
            new() { Path = @"%LOCALAPPDATA%\{ProcessName}", Category = "logs", FilePattern = "*.log", Recursive = true, MaxAgeDays = 7, MaxFiles = 50 },
            new() { Path = @"%APPDATA%\{ProcessName}", Category = "autosave", FilePattern = "*autosave*", Recursive = true, MaxAgeDays = 30, MaxFiles = 50 },
            new() { Path = @"%APPDATA%\{ProcessName}", Category = "backup", FilePattern = "*.bak", Recursive = true, MaxAgeDays = 30, MaxFiles = 50 },
            new() { Path = @"%LOCALAPPDATA%\{ProcessName}", Category = "autosave", FilePattern = "*autosave*", Recursive = true, MaxAgeDays = 30, MaxFiles = 50 },
            new() { Path = @"%TEMP%", Category = "temp", FilePattern = "{ProcessName}*", MaxAgeDays = 3, MaxFiles = 25 },
            new() { Path = @"{ExeDir}\logs", Category = "logs", FilePattern = "*.log", Recursive = true, MaxAgeDays = 7, MaxFiles = 50 },
        ],
    };

    public static IReadOnlyList<RescueProfile> All { get; } =
    [
        Universal,

        new()
        {
            Id = "blender",
            DisplayName = "Blender",
            ProcessNames = ["blender"],
            ArtifactRules =
            [
                // Blender's autosaves and quit.blend land in %TEMP% by default
                new() { Path = @"%TEMP%", Category = "autosave", FilePattern = "*.blend", MaxAgeDays = 14, MaxFiles = 25 },
                new() { Path = @"%TEMP%", Category = "autosave", FilePattern = "quit.blend", MaxAgeDays = 0, MaxFiles = 1 },
                new() { Path = @"%APPDATA%\Blender Foundation\Blender", Category = "config", FilePattern = "*.blend", Recursive = true, MaxAgeDays = 14, MaxFiles = 25 },
            ],
        },

        new()
        {
            Id = "roblox-studio",
            DisplayName = "Roblox Studio",
            ProcessNames = ["RobloxStudioBeta", "RobloxStudio"],
            ArtifactRules =
            [
                new() { Path = @"{Documents}\ROBLOX\AutoSaves", Category = "autosave", Recursive = true, MaxAgeDays = 30, MaxFiles = 50 },
                new() { Path = @"%LOCALAPPDATA%\Roblox\logs", Category = "logs", FilePattern = "*.log", MaxAgeDays = 3, MaxFiles = 25 },
            ],
        },

        new()
        {
            Id = "visual-studio",
            DisplayName = "Visual Studio",
            ProcessNames = ["devenv"],
            ArtifactRules =
            [
                new() { Path = @"{Documents}\Visual Studio*\Backup Files", Category = "backup", Recursive = true, MaxAgeDays = 14, MaxFiles = 100 },
                new() { Path = @"%APPDATA%\Microsoft\VisualStudio\*", Category = "logs", FilePattern = "ActivityLog*.xml", MaxAgeDays = 7, MaxFiles = 10 },
            ],
        },

        new()
        {
            Id = "vscode",
            DisplayName = "Visual Studio Code",
            ProcessNames = ["Code", "Code - Insiders"],
            ArtifactRules =
            [
                // Hot-exit backups of unsaved editors
                new() { Path = @"%APPDATA%\Code\Backups", Category = "autosave", Recursive = true, MaxAgeDays = 14, MaxFiles = 200 },
                new() { Path = @"%APPDATA%\Code\logs", Category = "logs", Recursive = true, MaxAgeDays = 2, MaxFiles = 50 },
            ],
        },

        new()
        {
            Id = "notepad-plus-plus",
            DisplayName = "Notepad++",
            ProcessNames = ["notepad++"],
            ArtifactRules =
            [
                // Unsaved tabs live here in full
                new() { Path = @"%APPDATA%\Notepad++\backup", Category = "autosave", Recursive = true, MaxAgeDays = 0, MaxFiles = 200 },
            ],
        },

        new()
        {
            Id = "ms-office",
            DisplayName = "Microsoft Office (Word/Excel/PowerPoint)",
            ProcessNames = ["WINWORD", "EXCEL", "POWERPNT"],
            ArtifactRules =
            [
                new() { Path = @"%LOCALAPPDATA%\Microsoft\Office\UnsavedFiles", Category = "autosave", MaxAgeDays = 30, MaxFiles = 50 },
                new() { Path = @"%APPDATA%\Microsoft\Word", Category = "autosave", FilePattern = "*.asd", MaxAgeDays = 30, MaxFiles = 25 },
                new() { Path = @"%APPDATA%\Microsoft\Excel", Category = "autosave", FilePattern = "*.xar", MaxAgeDays = 30, MaxFiles = 25 },
            ],
        },

        new()
        {
            Id = "chrome",
            DisplayName = "Google Chrome",
            ProcessNames = ["chrome"],
            ArtifactRules =
            [
                // Session files allow tab recovery after a hard kill
                new() { Path = @"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Sessions", Category = "session", MaxAgeDays = 7, MaxFiles = 10 },
                new() { Path = @"%LOCALAPPDATA%\Google\Chrome\User Data\Crashpad\reports", Category = "crash-dumps", MaxAgeDays = 7, MaxFiles = 5 },
            ],
        },

        new()
        {
            Id = "discord",
            DisplayName = "Discord",
            ProcessNames = ["Discord"],
            ArtifactRules =
            [
                new() { Path = @"%APPDATA%\discord", Category = "logs", FilePattern = "*.log", Recursive = true, MaxAgeDays = 3, MaxFiles = 25 },
            ],
        },
    ];
}
