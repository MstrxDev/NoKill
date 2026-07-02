using NoKill.Vault;

namespace NoKill.Profiles;

/// <summary>One-call façade for callers: process in, concrete artifact plan out.</summary>
public sealed class ArtifactPlanner
{
    private readonly ProfileStore _store;

    public ArtifactPlanner(ProfileStore? store = null)
    {
        _store = store ?? new ProfileStore();
    }

    public sealed record Plan(
        IReadOnlyList<ArtifactSource> Artifacts,
        IReadOnlyList<string> AppliedProfiles,
        IReadOnlyList<string> Warnings);

    public Plan PlanFor(string processName, string? executablePath)
    {
        var (profiles, warnings) = _store.LoadAll();
        var context = new ProcessContext(processName, executablePath);

        var applied = profiles
            .Where(p => p.AppliesTo(processName))
            .Select(p => p.DisplayName)
            .ToList();

        var artifacts = ArtifactCollector.Collect(profiles, context);

        return new Plan(artifacts, applied, warnings);
    }
}
