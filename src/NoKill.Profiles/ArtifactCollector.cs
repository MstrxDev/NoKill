using NoKill.Vault;

namespace NoKill.Profiles;

/// <summary>The process being rescued, as far as artifact discovery is concerned.</summary>
public sealed record ProcessContext(string ProcessName, string? ExecutablePath);

/// <summary>
/// Turns profile rules into a concrete, existing, capped list of files for
/// the vault to copy. Discovery only — nothing here writes, and paths that
/// don't exist are simply not matches (a heuristic that finds nothing is
/// normal, not an error).
/// </summary>
public static class ArtifactCollector
{
    public static IReadOnlyList<ArtifactSource> Collect(
        IEnumerable<RescueProfile> profiles, ProcessContext context)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<ArtifactSource>();

        foreach (var profile in profiles.Where(p => p.AppliesTo(context.ProcessName)))
        {
            foreach (var rule in profile.ArtifactRules)
            {
                foreach (string file in EvaluateRule(rule, context))
                {
                    if (seen.Add(file))
                    {
                        sources.Add(new ArtifactSource(file, rule.Category));
                    }
                }
            }
        }

        return sources;
    }

    private static IEnumerable<string> EvaluateRule(ArtifactRule rule, ProcessContext context)
    {
        string expandedPath;
        string pattern;
        try
        {
            expandedPath = ExpandTokens(rule.Path, context);
            pattern = rule.FilePattern is null ? "*" : ExpandTokens(rule.FilePattern, context);
        }
        catch
        {
            yield break; // {ExeDir} without a known executable path, etc.
        }

        DateTime? cutoff = rule.MaxAgeDays > 0 ? DateTime.UtcNow.AddDays(-rule.MaxAgeDays) : null;
        var matches = new List<(string Path, DateTime Modified)>();

        foreach (string directory in ExpandWildcardDirectories(expandedPath))
        {
            CollectFromDirectory(directory, pattern, rule.Recursive, cutoff, matches);
        }

        // Newest first, then cap: when a rule over-matches, the most recently
        // touched files are the ones most likely to hold unsaved work.
        foreach (var match in matches
            .OrderByDescending(m => m.Modified)
            .Take(rule.MaxFiles > 0 ? rule.MaxFiles : int.MaxValue))
        {
            yield return match.Path;
        }
    }

    private static void CollectFromDirectory(
        string directory, string pattern, bool recursive, DateTime? cutoff,
        List<(string, DateTime)> matches)
    {
        try
        {
            if (File.Exists(directory))
            {
                // The rule pointed directly at a file
                var modified = File.GetLastWriteTimeUtc(directory);
                if (cutoff is null || modified >= cutoff)
                {
                    matches.Add((directory, modified));
                }

                return;
            }

            if (!Directory.Exists(directory))
            {
                return;
            }

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive,
            };

            foreach (string file in Directory.EnumerateFiles(directory, pattern, options))
            {
                var modified = File.GetLastWriteTimeUtc(file);
                if (cutoff is null || modified >= cutoff)
                {
                    matches.Add((file, modified));
                }
            }
        }
        catch
        {
            // Access denied or racing deletions: this directory contributes nothing.
        }
    }

    private static string ExpandTokens(string template, ProcessContext context)
    {
        string result = template
            .Replace("{ProcessName}", context.ProcessName, StringComparison.OrdinalIgnoreCase)
            .Replace("{Documents}",
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                StringComparison.OrdinalIgnoreCase);

        if (result.Contains("{ExeDir}", StringComparison.OrdinalIgnoreCase))
        {
            string? exeDir = Path.GetDirectoryName(context.ExecutablePath);
            if (string.IsNullOrEmpty(exeDir))
            {
                throw new InvalidOperationException("Executable path unknown; {ExeDir} rule skipped.");
            }

            result = result.Replace("{ExeDir}", exeDir, StringComparison.OrdinalIgnoreCase);
        }

        return Environment.ExpandEnvironmentVariables(result);
    }

    /// <summary>
    /// Expands wildcard DIRECTORY segments, e.g.
    /// "C:\Users\x\Documents\Visual Studio*\Backup Files" → one path per
    /// matching directory. Segments without wildcards pass through.
    /// </summary>
    private static IEnumerable<string> ExpandWildcardDirectories(string path)
    {
        if (!path.Contains('*') && !path.Contains('?'))
        {
            return [path];
        }

        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = new List<string> { segments[0] + Path.DirectorySeparatorChar };

        foreach (string segment in segments.Skip(1))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            var next = new List<string>();
            foreach (string baseDir in current)
            {
                if (segment.Contains('*') || segment.Contains('?'))
                {
                    try
                    {
                        if (Directory.Exists(baseDir))
                        {
                            next.AddRange(Directory.EnumerateDirectories(baseDir, segment));
                        }
                    }
                    catch
                    {
                        // inaccessible parent: no expansions from here
                    }
                }
                else
                {
                    next.Add(Path.Combine(baseDir, segment));
                }
            }

            current = next;
        }

        return current;
    }
}
