using FluentAssertions;
using NexusMods.Paths;

namespace NexusMods.Games.RedEngine.Tests;

/// <summary>
/// The command line of redmod on Windows. Wine and Proton get theirs from <see cref="RedModDeployToolWineTests"/>.
/// </summary>
public class RedModDeployToolWindowsTests
{
    private static readonly AbsolutePath TempRoot = FileSystem.Shared.GetKnownPath(KnownPath.TempDirectory);

    [Fact]
    public void RootAndModListAreQuotedInsideTheValue()
    {
        var gameFolder = TempRoot.Combine("Steam Library").Combine("Cyberpunk 2077");
        var modList = TempRoot.Combine("temp dir").Combine("modlist.tmp");

        var arguments = RedModDeployTool.BuildDeployArguments(gameFolder, modList);

        // redmod only understands -name="value". Quoting the whole argument ("-name=value") makes it ignore -root, and
        // -modlist silently falls back to deploying every installed mod.
        arguments.Should().Be($"deploy -force -root=\"{gameFolder}\" -modlist=\"{modList}\"");
        arguments.Should().NotContain("\"-root");
        arguments.Should().NotContain("\"-modlist");
    }

    [Fact]
    public void RootIsAlwaysPassed()
    {
        var arguments = RedModDeployTool.BuildDeployArguments(TempRoot.Combine("Cyberpunk2077"), TempRoot.Combine("modlist.tmp"));

        // Without -root, redmod 2.5 finds no mods: "No mods found, no deployment is needed!"
        arguments.Should().StartWith("deploy ");
        arguments.Should().Contain(" -root=\"");
    }
}
