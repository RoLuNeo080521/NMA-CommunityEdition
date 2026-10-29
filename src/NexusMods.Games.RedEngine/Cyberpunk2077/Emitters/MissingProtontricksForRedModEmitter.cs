using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Diagnostics;
using NexusMods.Abstractions.Diagnostics.Emitters;
using NexusMods.Abstractions.Diagnostics.Values;
using NexusMods.Games.Generic.Dependencies;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Loadouts;
namespace NexusMods.Games.RedEngine.Cyberpunk2077.Emitters;

public partial class MissingProtontricksForRedModEmitter : ILoadoutDiagnosticEmitter
{
    public static readonly NamedLink ProtontricksLink = new("Protontricks Installation Guide", new Uri("https://github.com/Matoking/protontricks?tab=readme-ov-file#installation"));
    
    /// <summary>
    /// This will be null on non-Linux OSes.
    /// </summary>
    private AggregateProtontricksDependency? _protontricksDependency;
    
    /// <summary/>
    public MissingProtontricksForRedModEmitter(IServiceProvider serviceProvider) => _protontricksDependency = serviceProvider.GetService<AggregateProtontricksDependency>();

    public async IAsyncEnumerable<Diagnostic> Diagnose(
        Loadout.ReadOnly loadout,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!FileSystem.Shared.OS.IsLinux || _protontricksDependency == null)
            yield break;

        // Protontricks runs REDmod in the Proton prefix of the Steam game (RedModDeployTool), when there are REDmods to deploy
        if (loadout.InstallationInstance.LocatorResult.Store != GameStore.Steam)
            yield break;
        if (!MissingRedModEmitter.HasRedMods(loadout, out _, out _))
            yield break;

        // If there is no REDmod EXE, we don't need Protontricks: MissingRedModEmitter asks for the REDmod DLC first.
        if (!MissingRedModEmitter.HasRedModToolInstalled(loadout, out _))
            yield break;

        var installInfo = await _protontricksDependency.QueryInstallationInformation(cancellationToken);
        if (!installInfo.HasValue)
            yield return Diagnostics.CreateMissingProtontricksForRedMod(ProtontricksLink);
    }
}
