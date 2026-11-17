using TaskBarGame.Core;

namespace TaskBarGame.Core.Tests;

public class UpdatesTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string Release(string tag, bool draft = false, bool prerelease = false, string? digest = "sha256:" + Sha) => $$"""
        {
          "tag_name": "{{tag}}", "name": "TaskBarGame: Harvest Edition {{tag}}", "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}}, "html_url": "https://github.com/TheUthaGuy/TaskBarGame/releases/tag/{{tag}}",
          "body": "What's new in {{tag}}",
          "assets": [
            { "name": "TaskBarGame-HarvestEdition.exe", "size": 70000000,
              "browser_download_url": "https://github.com/TheUthaGuy/TaskBarGame/releases/download/{{tag}}/TaskBarGame-HarvestEdition.exe"
              {{(digest is null ? "" : $", \"digest\": \"{digest}\"")}} },
            { "name": "TaskBarGame-HarvestEdition.exe.sha256",
              "browser_download_url": "https://github.com/TheUthaGuy/TaskBarGame/releases/download/{{tag}}/TaskBarGame-HarvestEdition.exe.sha256" }
          ]
        }
        """;

    private static string List(params string[] releases) => "[" + string.Join(",", releases) + "]";

    [Fact]
    public void Picks_the_newest_harvest_release_and_ignores_the_original_edition()
    {
        string json = List(Release("v0.9.0"), Release("harvest-v0.2.0"), Release("harvest-v0.10.1"), Release("harvest-v0.3.0"));

        var newest = Updates.Newest(json)!;

        Assert.Equal(new Version(0, 10, 1), newest.Version); // 0.10.1 is newer than 0.3.0
        Assert.Equal("harvest-v0.10.1", newest.Tag);
        Assert.Equal(Sha, newest.Sha256);
        Assert.Equal(70_000_000, newest.ExeSize);
        Assert.EndsWith("/harvest-v0.10.1/TaskBarGame-HarvestEdition.exe", newest.ExeUrl);
        Assert.EndsWith(".sha256", newest.ChecksumUrl);
        Assert.Equal("What's new in harvest-v0.10.1", newest.Notes);
    }

    [Fact]
    public void Drafts_and_prereleases_are_skipped()
    {
        string json = List(Release("harvest-v0.4.0", draft: true), Release("harvest-v0.3.0", prerelease: true), Release("harvest-v0.2.0"));
        Assert.Equal(new Version(0, 2, 0), Updates.Newest(json)!.Version);
        Assert.Null(Updates.Newest(List(Release("v0.1.0"))));
    }

    [Fact]
    public void Unreadable_responses_mean_no_update()
    {
        Assert.Null(Updates.Newest("not json"));
        Assert.Null(Updates.Newest("""{ "message": "API rate limit exceeded" }"""));
        Assert.Null(Updates.Newest("[]"));
    }

    [Fact]
    public void Versions_compare_the_way_people_read_them()
    {
        var release = Updates.Newest(List(Release("harvest-v0.3.0")))!;
        Assert.True(Updates.IsNewer(release, new Version(0, 2, 0, 0)));
        Assert.False(Updates.IsNewer(release, new Version(0, 3, 0, 0))); // assembly versions have 4 parts
        Assert.False(Updates.IsNewer(release, new Version(1, 0)));
        Assert.Null(Updates.VersionFromTag("harvest-vbanana"));
        Assert.Equal(new Version(1, 2, 0), Updates.VersionFromTag("harvest-v1.2"));
    }

    [Fact]
    public void Checksums_are_read_from_github_digests_or_checksum_files()
    {
        Assert.Equal(Sha, Updates.Sha256FromDigest("sha256:" + Sha.ToUpperInvariant()));
        Assert.Null(Updates.Sha256FromDigest("md5:abc"));
        Assert.Null(Updates.Sha256FromDigest(null));
        Assert.Equal(Sha, Updates.ParseSha256($"{Sha}  TaskBarGame-HarvestEdition.exe\n"));
        Assert.Null(Updates.ParseSha256("too short"));

        var noDigest = Updates.Newest(List(Release("harvest-v0.3.0", digest: null)))!;
        Assert.Null(noDigest.Sha256);
        Assert.NotNull(noDigest.ChecksumUrl);
    }

    [Fact]
    public void Updates_only_download_from_this_repos_github_releases()
    {
        Assert.True(Updates.IsTrustedDownload("https://github.com/TheUthaGuy/TaskBarGame/releases/download/harvest-v0.3.0/TaskBarGame-HarvestEdition.exe"));
        Assert.False(Updates.IsTrustedDownload("http://github.com/TheUthaGuy/TaskBarGame/releases/download/harvest-v0.3.0/TaskBarGame-HarvestEdition.exe"));
        Assert.False(Updates.IsTrustedDownload("https://github.com/someone-else/TaskBarGame/releases/download/x/TaskBarGame-HarvestEdition.exe"));
        Assert.False(Updates.IsTrustedDownload("https://example.com/TaskBarGame-HarvestEdition.exe"));
        Assert.False(Updates.IsTrustedDownload(null));
    }
}
