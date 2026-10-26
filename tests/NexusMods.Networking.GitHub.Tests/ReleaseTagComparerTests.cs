using FluentAssertions;
using NexusMods.Networking.GitHub.DTOs;
using Xunit;

namespace NexusMods.Networking.GitHub.Tests;

public class ReleaseTagComparerTests
{
    private static Release CreateRelease(string tagName) => new(
        Url: "", AssetsUrl: "", HtmlUrl: "", Id: 0, TagName: tagName, TargetCommitish: "", Name: tagName,
        IsDraft: false, IsPrerelease: false, CreatedAt: default, PublishedAt: default, Assets: [],
        TarballUrl: "", ZipballUrl: "", Body: ""
    );

    [Theory]
    [InlineData("v1.1.0", "1.1.0")]
    [InlineData("V1.0.1", "1.0.1")]
    [InlineData("1.2.0", "1.2.0")]
    public void Test_TryGetVersion(string tagName, string expectedVersion)
    {
        CreateRelease(tagName).TryGetVersion(out var version).Should().BeTrue();
        version.Should().Be(Version.Parse(expectedVersion));
    }

    [Theory]
    // The releases of the fork, in the order the GitHub API returns them (newest first)
    [InlineData("v1.1.0", new[] { "v1.1.0", "V1.0.1", "V1.0.0" })]
    [InlineData("v1.2.0", new[] { "v1.2.0", "v1.1.0", "V1.0.1", "V1.0.0" })]
    [InlineData("v1.2.0", new[] { "V1.0.0", "nightly", "v1.2.0", "v1.1.0" })]
    [InlineData("v1.2.0", new[] { "nightly", "v1.2.0", "latest", "v1.1.0" })]
    public void Test_LatestRelease(string expectedLatest, string[] tagNames)
    {
        var releases = tagNames.Select(CreateRelease).ToArray();
        Array.Sort(releases, ReleaseTagComparer.Instance);

        releases[^1].TagName.Should().Be(expectedLatest);
    }
}
