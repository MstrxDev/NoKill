namespace NoKill.Core.Updates;

/// <summary>Pure version/tag comparison for update checks. No I/O — fully testable.</summary>
public static class UpdateVersion
{
    /// <summary>"v0.2.0-beta" → "0.2.0"; trims, strips the leading v and any prerelease suffix.</summary>
    public static string Normalize(string tag)
    {
        string result = tag.Trim();
        if (result.StartsWith('v') || result.StartsWith('V'))
        {
            result = result[1..];
        }

        int dash = result.IndexOf('-');
        if (dash >= 0)
        {
            result = result[..dash];
        }

        return result;
    }

    /// <summary>
    /// True when <paramref name="candidateTag"/> is a strictly newer version
    /// than <paramref name="current"/>. Comparison uses the first three
    /// components; unparseable input is never "newer" (an update check must
    /// not fire on garbage).
    /// </summary>
    public static bool IsNewer(string current, string candidateTag)
    {
        var cur = TryParse3(current);
        var cand = TryParse3(candidateTag);
        return cur is not null && cand is not null && cand > cur;
    }

    private static Version? TryParse3(string text)
    {
        string[] parts = Normalize(text).Split('.');
        if (parts.Length == 0 || parts.Any(p => p.Length == 0))
        {
            return null;
        }

        int[] nums = new int[3];
        for (int i = 0; i < 3; i++)
        {
            if (i < parts.Length)
            {
                if (!int.TryParse(parts[i], out nums[i]) || nums[i] < 0)
                {
                    return null;
                }
            }
        }

        return new Version(nums[0], nums[1], nums[2]);
    }
}
