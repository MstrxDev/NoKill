using System.Text.Json;

namespace NoKill.Core.Updates;

/// <summary>A newer release found by the update check.</summary>
public sealed record UpdateInfo(string Version, string ReleaseUrl, string? InstallerUrl);

/// <summary>
/// Checks GitHub Releases for a newer NoKill version. Doctrine notes, because
/// NoKill promises local-first and no telemetry: this is the ONE outbound
/// network call in the product — an anonymous GET of the public releases feed
/// carrying nothing but the request itself. It is user-disableable, and every
/// failure path is silent: an update check must never break or slow a rescue.
/// </summary>
public sealed class UpdateChecker
{
    public const string DefaultReleasesEndpoint =
        "https://api.github.com/repos/MstrxDev/NoKill/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private readonly string _endpoint;

    public UpdateChecker(string? endpoint = null)
    {
        _endpoint = endpoint ?? DefaultReleasesEndpoint;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NoKill-UpdateCheck");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>Returns the newer release, or null (up to date, offline, API error — all silent).</summary>
    public async Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(_endpoint, cancellationToken));
            var root = doc.RootElement;

            string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            if (!UpdateVersion.IsNewer(currentVersion, tag))
            {
                return null;
            }

            string releaseUrl = root.GetProperty("html_url").GetString() ?? string.Empty;

            string? installerUrl = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString() ?? string.Empty;
                    if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                    {
                        installerUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            return new UpdateInfo(UpdateVersion.Normalize(tag), releaseUrl, installerUrl);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Downloads the release MSI to temp; null on any failure (caller falls back to the release page).</summary>
    public async Task<string?> DownloadInstallerAsync(UpdateInfo update, CancellationToken cancellationToken = default)
    {
        if (update.InstallerUrl is null)
        {
            return null;
        }

        try
        {
            string path = Path.Combine(Path.GetTempPath(), $"NoKill-{update.Version}.msi");
            await using (var stream = await Http.GetStreamAsync(update.InstallerUrl, cancellationToken))
            await using (var file = File.Create(path))
            {
                await stream.CopyToAsync(file, cancellationToken);
            }

            return path;
        }
        catch
        {
            return null;
        }
    }
}
