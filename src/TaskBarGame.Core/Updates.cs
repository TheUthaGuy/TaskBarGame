using System.Text.Json;

namespace TaskBarGame.Core;

/// <summary>A Harvest Edition release on GitHub that the game could update to.</summary>
/// <param name="ExeUrl">Where to download the new TaskBarGame-HarvestEdition.exe (null if the release has none).</param>
/// <param name="Sha256">The exe's SHA-256 as GitHub reports it (lowercase hex), used to check the download.</param>
/// <param name="ChecksumUrl">A ".sha256" file published with the release, for when GitHub doesn't report one.</param>
public sealed record ReleaseInfo(
    Version Version,
    string Tag,
    string Name,
    string PageUrl,
    string? ExeUrl,
    long ExeSize,
    string? Sha256,
    string? ChecksumUrl,
    string Notes);

/// <summary>
/// Finding new versions: the Harvest Edition is released on GitHub with tags like "harvest-v0.3.0"
/// (the original edition uses "v0.1.0" tags, which are ignored here).
/// </summary>
public static class Updates
{
    public const string Owner = "TheUthaGuy";
    public const string Repo = "TaskBarGame";

    /// <summary>Harvest Edition releases are tagged harvest-v1.2.3.</summary>
    public const string TagPrefix = "harvest-v";

    /// <summary>The file each Harvest Edition release carries (its game exe).</summary>
    public const string ExeAssetName = "TaskBarGame-HarvestEdition.exe";

    public const string ChecksumAssetName = ExeAssetName + ".sha256";

    /// <summary>The GitHub API address that lists releases, newest first.</summary>
    public static string ReleasesApiUrl => $"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page=30";

    public static string ReleasesPageUrl => $"https://github.com/{Owner}/{Repo}/releases";

    /// <summary>"harvest-v0.3.0" → 0.3.0; anything else → null.</summary>
    public static Version? VersionFromTag(string? tag)
    {
        if (tag is null || !tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        string text = tag[TagPrefix.Length..];
        if (!Version.TryParse(text, out var version) || version.Major < 0) return null;
        return Normalize(version);
    }

    /// <summary>1.2 → 1.2.0, and 1.2.3.0 → 1.2.3, so versions compare the way people read them.</summary>
    public static Version Normalize(Version version) =>
        new(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build));

    public static bool IsNewer(ReleaseInfo release, Version current) => release.Version > Normalize(current);

    /// <summary>
    /// The newest published Harvest Edition release in a GitHub "list releases" response.
    /// Drafts and pre-releases are skipped. Null if there isn't one (or the response isn't readable).
    /// </summary>
    public static ReleaseInfo? Newest(string releasesJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(releasesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            ReleaseInfo? best = null;
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (Bool(release, "draft") || Bool(release, "prerelease")) continue;
                string tag = Text(release, "tag_name") ?? "";
                if (VersionFromTag(tag) is not { } version) continue;
                if (best is not null && version <= best.Version) continue;

                string? exeUrl = null, sha = null, checksumUrl = null;
                long size = 0;
                if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = Text(asset, "name") ?? "";
                        if (name.Equals(ExeAssetName, StringComparison.OrdinalIgnoreCase))
                        {
                            exeUrl = Text(asset, "browser_download_url");
                            size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out long n) ? n : 0;
                            sha = Sha256FromDigest(Text(asset, "digest"));
                        }
                        else if (name.Equals(ChecksumAssetName, StringComparison.OrdinalIgnoreCase))
                        {
                            checksumUrl = Text(asset, "browser_download_url");
                        }
                    }
                }

                best = new ReleaseInfo(
                    version,
                    tag,
                    Text(release, "name") is { Length: > 0 } title ? title : $"Harvest Edition v{version}",
                    Text(release, "html_url") ?? ReleasesPageUrl,
                    exeUrl,
                    size,
                    sha,
                    checksumUrl,
                    Text(release, "body") ?? "");
            }
            return best;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>GitHub reports a file's checksum as "sha256:abc123…". Returns the hex part, or null.</summary>
    public static string? Sha256FromDigest(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        return ParseSha256(digest[prefix.Length..]);
    }

    /// <summary>
    /// Reads a SHA-256 from a checksum file ("abc123…  TaskBarGame-HarvestEdition.exe" or just the hex).
    /// Returns lowercase hex, or null if there isn't a valid one.
    /// </summary>
    public static string? ParseSha256(string? text)
    {
        if (text is null) return null;
        string first = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.Length == 64 && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Only download updates from GitHub over HTTPS (unless a test feed was set on purpose).
    /// </summary>
    public static bool IsTrustedDownload(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith($"/{Owner}/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase);

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
