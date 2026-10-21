using GodotLauncher.Core.Catalog;
using GodotLauncher.Core.Models;

namespace GodotLauncher.Core.Tests;

public sealed class VersionParsingTests
{
    [Theory]
    [InlineData("4.7.2", 4, 7, 2)]
    [InlineData("4.8", 4, 8, 0)]
    [InlineData("v3.6", 3, 6, 0)]
    [InlineData("4.8-beta1", 4, 8, 0)]
    public void TryParse_ReadsNumericParts(string text, int major, int minor, int patch)
    {
        Assert.True(VersionNumber.TryParse(text, out var version));
        Assert.Equal(new VersionNumber(major, minor, patch), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("1.2.3.4")]
    [InlineData("abc.def")]
    public void TryParse_RejectsInvalidValues(string text) =>
        Assert.False(VersionNumber.TryParse(text, out _));

    [Theory]
    [InlineData("stable", ReleaseChannel.Stable, 0)]
    [InlineData("stable-jam", ReleaseChannel.Stable, 0)]
    [InlineData("rc1", ReleaseChannel.Rc, 1)]
    [InlineData("rc12", ReleaseChannel.Rc, 12)]
    [InlineData("beta2", ReleaseChannel.Beta, 2)]
    [InlineData("dev7", ReleaseChannel.Dev, 7)]
    [InlineData("alpha3", ReleaseChannel.Dev, 3)]
    [InlineData("alpha-unofficial", ReleaseChannel.Dev, 0)]
    [InlineData("mystery", ReleaseChannel.Dev, 0)]
    public void ClassifyFlavor_MapsFlavorsToChannels(string flavor, ReleaseChannel channel, int number)
    {
        var result = ReleaseChannelExtensions.ClassifyFlavor(flavor);
        Assert.Equal(channel, result.Channel);
        Assert.Equal(number, result.Number);
    }

    [Fact]
    public void GodotVersion_ExposesIdAndDisplayName()
    {
        var version = GodotVersion.Create("4.8", "beta1", new DateOnly(2026, 10, 15));

        Assert.Equal("4.8-beta1", version.Id);
        Assert.Equal(ReleaseChannel.Beta, version.Channel);
        Assert.Equal("Godot 4.8 Beta1", version.DisplayName);
        Assert.Equal("15 October 2026", version.ReleaseDateText);
        Assert.True(version.IsPreview);
    }

    [Fact]
    public void GodotVersion_StableDisplayNameOmitsChannel()
    {
        var version = GodotVersion.Create("4.7.2", "stable");
        Assert.Equal("Godot 4.7.2", version.DisplayName);
        Assert.False(version.IsPreview);
    }

    [Theory]
    [InlineData("4.7.2-stable", "4.7.2", "stable")]
    [InlineData("4.8-beta1", "4.8", "beta1")]
    [InlineData("4.0-alpha-unofficial", "4.0", "alpha-unofficial")]
    public void VersionIds_SplitNameAndFlavor(string id, string name, string flavor)
    {
        Assert.True(VersionIds.TryParse(id, out var parsedName, out var parsedFlavor));
        Assert.Equal(name, parsedName);
        Assert.Equal(flavor, parsedFlavor);
    }

    [Theory]
    [InlineData("stable")]
    [InlineData("")]
    [InlineData("4.7.2")]
    public void VersionIds_RejectMalformedIds(string id) =>
        Assert.False(VersionIds.TryParse(id, out _, out _));

    [Fact]
    public void ReleaseDates_ParsesWebsiteFormat()
    {
        Assert.Equal(new DateOnly(2023, 5, 9), ReleaseDates.Parse("9 May 2023"));
        Assert.Equal(new DateOnly(2026, 10, 15), ReleaseDates.Parse("15 October 2026"));
        Assert.Equal(DateOnly.MinValue, ReleaseDates.Parse("not a date"));
        Assert.Equal(DateOnly.MinValue, ReleaseDates.Parse(null));
    }
}

public sealed class CatalogNormalizerTests
{
    private static GodotVersion V(string name, string flavor, int year = 2026, int month = 1, int day = 1) =>
        GodotVersion.Create(name, flavor, new DateOnly(year, month, day));

    [Fact]
    public void Normalize_SortsNewestFirstAcrossChannels()
    {
        var versions = new[]
        {
            V("4.7.2", "stable"),
            V("4.8", "beta1"),
            V("4.8", "dev7"),
            V("4.8", "rc1"),
            V("4.7.2", "rc1"),
        };

        var result = CatalogNormalizer.Normalize(versions);

        Assert.Equal(
            new[] { "4.8-rc1", "4.8-beta1", "4.8-dev7", "4.7.2-stable", "4.7.2-rc1" },
            result.Select(v => v.Id));
    }

    [Fact]
    public void Normalize_RemovesDuplicateIdsKeepingFirst()
    {
        var first = V("4.7.2", "stable", 2026, 8, 18);
        var duplicate = V("4.7.2", "stable", 2020, 1, 1);

        var result = CatalogNormalizer.Normalize([first, duplicate]);

        Assert.Single(result);
        Assert.Equal(new DateOnly(2026, 8, 18), result[0].ReleaseDate);
    }

    [Fact]
    public void Normalize_DropsInvalidEntries()
    {
        var invalid = new GodotVersion { Name = "nonsense", Flavor = "stable" };
        var result = CatalogNormalizer.Normalize([V("4.7.2", "stable"), invalid]);
        Assert.Single(result);
    }

    [Fact]
    public void Normalize_UsesStageNumberWithinSameChannel()
    {
        var result = CatalogNormalizer.Normalize([V("4.8", "beta1"), V("4.8", "beta3")]);
        Assert.Equal("4.8-beta3", result[0].Id);
    }

    [Theory]
    [InlineData(100, 49, true)]
    [InlineData(100, 51, false)]
    [InlineData(8, 1, false)]
    [InlineData(0, 0, false)]
    public void LooksLikeTruncatedCatalog_AppliesSafetyValve(int cached, int fresh, bool expected) =>
        Assert.Equal(expected, CatalogNormalizer.LooksLikeTruncatedCatalog(cached, fresh));
}

public sealed class VersionsYmlParserTests
{
    [Fact]
    public void Parse_ReadsTheBundledOfficialCatalog()
    {
        var versions = new VersionsYmlParser().Parse(TestData.ReadVersionsYml());

        Assert.True(versions.Count > 300, $"Expected more than 300 versions but found {versions.Count}.");

        var ids = versions.Select(v => v.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.Contains(versions, v => v.Id == "4.8-beta1");
        Assert.Contains(versions, v => v.Id == "4.7.2-stable");
        Assert.Contains(versions, v => v.Id == "3.6.3-stable");
        Assert.Contains(versions, v => v.Id == "4.7-rc3");
        Assert.All(versions, v => Assert.True(v.Number.Major >= 1));
    }

    [Fact]
    public void Parse_ProducesNewestFirstOrder()
    {
        var versions = new VersionsYmlParser().Parse(TestData.ReadVersionsYml());
        var sorted = versions.ToList();
        sorted.Sort(VersionComparer.Instance);

        Assert.Equal(versions.Select(v => v.Id), sorted.Select(v => v.Id));
        Assert.StartsWith("4.8", versions[0].Id);
    }

    [Fact]
    public void Parse_KeepsFeaturedFlag()
    {
        var versions = new VersionsYmlParser().Parse(TestData.ReadVersionsYml());
        Assert.Contains(versions, v => v.Id == "4.7.2-stable" && v.IsFeatured);
    }

    [Fact]
    public void Parse_PromotesNestedPreviewsToTheirOwnEntries()
    {
        var versions = new VersionsYmlParser().Parse("""
            - name: "4.8"
              flavor: "beta1"
              release_date: "15 October 2026"
              release_notes: "/article/beta/"
              releases:
                - name: "dev2"
                  release_date: "21 July 2026"
                - name: "dev1"
                  release_date: "6 July 2026"
            """);

        Assert.Equal(new[] { "4.8-beta1", "4.8-dev2", "4.8-dev1" }, versions.Select(v => v.Id));
        Assert.Equal(2, versions[0].PatchReleases.Count);
    }

    [Fact]
    public void Parse_IgnoresNumericNestedNames()
    {
        var versions = new VersionsYmlParser().Parse("""
            - name: "4.8"
              flavor: "beta1"
              releases:
                - name: "4.7.2"
                  release_date: "18 August 2026"
            """);

        Assert.Single(versions);
    }

    [Fact]
    public void Parse_HandlesMissingFieldsGracefully()
    {
        var versions = new VersionsYmlParser().Parse("""
            - name: "4.8"
              flavor: "beta1"
            - name: "broken"
              flavor: "stable"
            - flavor: "stable"
            """);

        Assert.Single(versions);
        Assert.Equal(DateOnly.MinValue, versions[0].ReleaseDate);
    }

    [Fact]
    public void Parse_ThrowsOnInvalidYaml()
    {
        var parser = new VersionsYmlParser();
        Assert.Throws<FormatException>(() => parser.Parse("- name: \"4.8\"\n  flavor: [unclosed"));
    }
}
