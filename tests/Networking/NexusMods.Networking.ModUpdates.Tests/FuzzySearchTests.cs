using FluentAssertions;
namespace NexusMods.Networking.ModUpdates.Tests;

/// <summary>
/// Tests based around fuzzy searching of updates for files based on filenames.
/// </summary>
public class FuzzySearchTests
{
    [Theory]
    // Reference Example: SkyUI (underscores and version in middle)
    [InlineData("SkyUI_5_2_SE", "5.2SE", "skyui se")]
    // Reference Example: Skyrim 202X (version after name)
    [InlineData("Skyrim 202X 9.0 - Architecture PART 1", "9.0", "skyrim 202x - architecture part 1")]
    // Reference Example: Quality World Map (file version with suffix)
    [InlineData("9.0 A Quality World Map - Paper", "9.0P", "a quality world map - paper")]
    // Reference Example: Maestros of Synth (extension stripping)
    [InlineData("Maestros of Synth.zip", "", "maestros of synth")]
    // USSEP style with underscores and extension
    [InlineData("Unofficial_Skyrim_Special_Edition_Patch.rar", "", "unofficial skyrim special edition patch")]
    // Multiple spaces normalization
    [InlineData("Mod    Name  with   multiple    spaces", "", "mod name with multiple spaces")]
    // Version with non-semver beta suffix
    [InlineData("Mod 1.2beta.zip", "1.2beta", "mod")]
    // Version with non-semver suffix and underscores
    [InlineData("Cool_Mod_1_2alpha.7z", "1.2alpha", "cool mod")]
    // (Sewer) Edge cases we don't currently handle:
    // - Version with space between number and suffix in name
    //      - e.g. 'Cool Mod 1.2 alpha.7z' + '1.2alpha' will not eliminate '1.2 alpha', because version doesn't have a space in name.
    public void NormalizeFileName_ShouldHandleVariousCases(string fileName, string version, string expected)
    {
        // Act
        var result = FuzzySearch.NormalizeFileName(fileName, version);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "1.0", "")] // Null input
    [InlineData("test", null, "test")] // Null Version
    [InlineData("", "1.0", "")] // Empty string
    [InlineData(" ", "1.0", "")] // Whitespace only
    public void NormalizeFileName_ShouldHandleEmptyInput(string? fileName, string version, string expected)
    {
        // Act
        var result = FuzzySearch.NormalizeFileName(fileName, version);

        // Assert
        result.Should().Be(expected);
    }
    
    [Theory]
    // Suffix
    [InlineData("5.2SE", new[] { "5.2SE", "5.2", "5_2SE", "5_2" })]
    [InlineData("9.0P", new[] { "9.0P", "9.0", "9_0P", "9_0" })]
    [InlineData("9.0VF", new[] { "9.0VF", "9.0", "9_0VF", "9_0" })]
    // Prefix
    [InlineData("v5.2", new[] { "v5.2", "5.2", "v5_2", "5_2" })]
    // Standard
    [InlineData("10.0.1", new[] { "10.0.1", "10_0_1" })]
    // Invalid
    [InlineData("", new string[] { })]
    [InlineData(null, new string[] { })]
    // Prefix and Suffix
    [InlineData("v1alpha", new[] { "v1alpha", "1alpha", "v1", "1" })]
    [InlineData("v5.2SE", new[] { "v5.2SE", "5.2SE", "v5.2", "5.2", "v5_2SE", "5_2SE", "v5_2", "5_2" })]
    [InlineData("v1.0beta", new[] { "v1.0beta", "1.0beta", "v1.0", "1.0", "v1_0beta", "1_0beta", "v1_0", "1_0" })]
    // SemVer Prereleases
    [InlineData("1.0-alpha", new[] { "1.0-alpha", "1.0", "1_0-alpha", "1_0" })]
    [InlineData("v1.0-alpha", new[] { "v1.0-alpha", "1.0-alpha", "v1.0", "1.0", "v1_0-alpha", "1_0-alpha", "v1_0", "1_0" })]
    public void GetVersionPermutations_ShouldReturnAllPossibleVersionFormats(string input, string[] expected)
    {
        // Act
        var result = FuzzySearch.GetVersionPermutations(input);

        // Assert
        result.Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData("Maestros of Synth.zip", "Maestros of Synth")]
    [InlineData("Maestros of Synth.ZIP", "Maestros of Synth")]
    [InlineData("Maestros of Synth.7z", "Maestros of Synth")]
    [InlineData("Maestros of Synth.omod", "Maestros of Synth")]
    [InlineData("Maestros of Synth", "Maestros of Synth")]
    [InlineData("Something.with.dots.zip", "Something.with.dots")]
    [InlineData("Maestros of Synth.nx", "Maestros of Synth")] // maybe one day
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    public void StripFileExtensions_ShouldHandleAllowedFileExtension(string input, string expected)
    {
        // Act
        var result = FuzzySearch.StripFileExtension(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    // Version tag and punctuation dropped
    [InlineData("Cool Widget - Extended V2", "2.0.1", "cool widget extended")]
    [InlineData("Cool Widget (Lite) v1.3b.zip", "1.0", "cool widget lite")]
    // Game version tag dropped
    [InlineData("Cool Widget 1.1 for 2.50", "1.1", "cool widget")]
    [InlineData("Cool Widget for game 2.3a", "1.0", "cool widget")]
    // Trailing dot after a version-like token
    [InlineData("Cool Widget - 2.31.", "0.14.0", "cool widget")]
    // Bare numbers are kept: "Part 1" and "Part 2" are different files
    [InlineData("Cool Widget - Part 2", "1.0", "cool widget part 2")]
    [InlineData(null, "1.0", "")]
    public void NormalizeFileNameLoose_ShouldDropTagsAndPunctuation(string? fileName, string version, string expected)
    {
        FuzzySearch.NormalizeFileNameLoose(fileName, version).Should().Be(expected);
    }

    [Theory]
    // A file renamed between two uploads: a tag was added and the version changed
    [InlineData("Cool Widget - Extended", "1.3.0", "Cool Widget - Extended V2", "2.0.1", true)]
    // The game version tag changes together with the version
    [InlineData("Cool Widget 1.0 for 2.31", "1.0", "Cool Widget 1.1 for 2.50", "1.1", true)]
    // A trailing dot after a version-like token
    [InlineData("Cool Widget - 2.31.", "0.14.0", "Cool Widget - 2.50.", "0.14.1", true)]
    // Equal after the strict normalization
    [InlineData("Cool Widget 1.0.zip", "1.0", "Cool Widget 2.0.zip", "2.0", true)]
    // Same version number: names that only differ by a tag are different files, not a rename
    [InlineData("Cool Widget V1", "1.0", "Cool Widget V2", "1.0", false)]
    // Different files
    [InlineData("Cool Widget - Part 1", "1.0", "Cool Widget - Part 2", "1.1", false)]
    [InlineData("Cool Widget", "1.0", "Other Widget", "1.1", false)]
    public void IsSameFileLineage_ShouldRecognizeRenamedFiles(string nameA, string versionA, string nameB, string versionB, bool expected)
    {
        FuzzySearch.IsSameFileLineage(nameA, versionA, nameB, versionB).Should().Be(expected);
    }

    [Theory]
    // A renamed file needs a strictly higher version
    [InlineData("Cool Widget - Extended", "1.3.0", "Cool Widget - Extended V2", "2.0.1", true)]
    [InlineData("Cool Widget - Extended V2", "2.0.1", "Cool Widget - Extended", "1.3.0", false)]
    [InlineData("Cool Widget - Extended", "1.3.0", "Cool Widget - Extended V2", "1.3.0", false)]
    // Unrelated files are never newer versions
    [InlineData("Cool Widget", "1.0", "Other Widget", "2.0", false)]
    public void IsNewerFileInSameLineage_ShouldRequireAHigherVersionForRenamedFiles(string oldName, string oldVersion, string newName, string newVersion, bool expected)
    {
        FuzzySearch.IsNewerFileInSameLineage(oldName, oldVersion, newName, newVersion).Should().Be(expected);
    }

    [Theory]
    [InlineData("1.2.10", "1.2.9", true)]
    [InlineData("1.2.9", "1.2.10", false)]
    [InlineData("2.0.1", "2", true)]
    [InlineData("2.0", "2", false)]
    [InlineData("1.0", "1.0", false)]
    [InlineData("v2", "1.9", true)]
    [InlineData("2.0", null, true)]
    [InlineData("", "1.0", false)]
    [InlineData(null, "1.0", false)]
    public void VersionIsGreater_ShouldCompareNumericParts(string? candidate, string? baseline, bool expected)
    {
        FuzzySearch.VersionIsGreater(candidate, baseline).Should().Be(expected);
    }
}
