using System;
using IRacingDirector.Services;
using Xunit;

namespace IRacingDirector.Tests;

public class UpdateCheckerTests
{
    private static readonly Version Current = new(1, 1, 0, 0);

    [Theory]
    [InlineData("v1.1.1", true)]
    [InlineData("v1.2.0", true)]
    [InlineData("v2.0.0", true)]
    [InlineData("v1.10.0", true)]
    [InlineData("1.1.1", true)]
    [InlineData("v1.1.0", false)]
    [InlineData("v1.1", false)]
    [InlineData("v1.0.9", false)]
    [InlineData("v0.9.9", false)]
    public void ComparesTagWithCurrentVersion(string tag, bool newer) =>
        Assert.Equal(newer, UpdateChecker.IsNewer(tag, Current));

    [Fact]
    public void ComparesNumericallyNotAlphabetically() =>
        Assert.True(UpdateChecker.IsNewer("v1.10.0", new Version(1, 9, 2)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1")]
    [InlineData("v1.2.3.4")]
    [InlineData("v1.2.3-beta")]
    [InlineData("v1.x.0")]
    public void RejectsUnusableTags(string? tag)
    {
        Assert.False(UpdateChecker.TryParseTag(tag, out _));
        Assert.False(UpdateChecker.IsNewer(tag, Current));
    }

    [Fact]
    public void ParsesTagWithOrWithoutPrefix()
    {
        Assert.True(UpdateChecker.TryParseTag("v1.2.3", out var a));
        Assert.True(UpdateChecker.TryParseTag(" V1.2.3 ", out var b));
        Assert.True(UpdateChecker.TryParseTag("1.2", out var c));
        Assert.Equal(new Version(1, 2, 3), a);
        Assert.Equal(a, b);
        Assert.Equal(new Version(1, 2, 0), c);
    }

    [Fact]
    public void ParsesReleaseAndPicksSetupAsset()
    {
        const string json = """
        {
          "tag_name": "v1.2.0",
          "html_url": "https://github.com/3beeps/iRacingDirector/releases/tag/v1.2.0",
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://github.com/x/notes.txt" },
            { "name": "iRacingDirector-Setup-1.2.0.exe", "browser_download_url": "https://github.com/x/iRacingDirector-Setup-1.2.0.exe" }
          ]
        }
        """;
        var release = UpdateChecker.ParseRelease(json);
        Assert.NotNull(release);
        Assert.Equal(new Version(1, 2, 0), release!.Version);
        Assert.Equal("iRacingDirector-Setup-1.2.0.exe", release.SetupName);
        Assert.Equal("https://github.com/x/iRacingDirector-Setup-1.2.0.exe", release.SetupUrl);
        Assert.Null(release.Sha256);
    }

    [Fact]
    public void ReadsTheSetupChecksum()
    {
        const string json = """
        { "tag_name": "v1.3.0", "assets": [ { "name": "iRacingDirector-Setup-1.3.0.exe",
          "browser_download_url": "https://github.com/x/s.exe", "digest": "sha256:ABCDEF0123" } ] }
        """;
        Assert.Equal("abcdef0123", UpdateChecker.ParseRelease(json)!.Sha256);
    }

    [Theory]
    [InlineData("""{ "tag_name": "v1.2.0", "assets": [] }""")]
    [InlineData("""{ "tag_name": "v1.2.0", "assets": [ { "name": "iRacingDirector-Setup-1.2.0.exe", "browser_download_url": "http://github.com/x.exe" } ] }""")]
    [InlineData("""{ "tag_name": "v1.2.0", "assets": [ { "name": "..\\evil.exe", "browser_download_url": "https://github.com/x.exe" } ] }""")]
    [InlineData("""{ "tag_name": "nightly", "assets": [ { "name": "iRacingDirector-Setup-1.2.0.exe", "browser_download_url": "https://github.com/x.exe" } ] }""")]
    public void IgnoresReleasesWithoutUsableSetup(string json) =>
        Assert.Null(UpdateChecker.ParseRelease(json));
}
