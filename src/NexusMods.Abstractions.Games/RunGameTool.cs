using System.Diagnostics;
using CliWrap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Paths;
using NexusMods.Sdk;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Jobs;
using NexusMods.Sdk.Loadouts;
using R3;

namespace NexusMods.Abstractions.Games;

/// <summary>
/// Marker interface for RunGameTool
/// </summary>
public interface IRunGameTool : ITool;

/// <summary>
/// A tool that launches the game, using first found installation.
/// </summary>
/// <typeparam name="T"></typeparam>
public class RunGameTool<T> : IRunGameTool
    where T : IGame
{
    private readonly ILogger<RunGameTool<T>> _logger;
    private readonly T _game;
    private readonly IProcessRunner _processRunner;
    private readonly IOSInterop _osInterop;

    /// <summary>
    /// Whether this tool should be started through the shell instead of directly.
    /// This allows tools to start their own console, allowing users to interact with it.
    /// </summary>
    protected virtual bool UseShell { get; set; } = false;

    /// <summary>
    /// Constructor
    /// </summary>
    public RunGameTool(IServiceProvider serviceProvider, T game)
    {
        _game = game;
        _logger = serviceProvider.GetRequiredService<ILogger<RunGameTool<T>>>();
        _processRunner = serviceProvider.GetRequiredService<IProcessRunner>();
        _osInterop = serviceProvider.GetRequiredService<IOSInterop>();
    }

    /// <inheritdoc />
    public IEnumerable<GameId> GameIds => [_game.GameId];

    /// <inheritdoc />
    public string Name => $"Run {_game.DisplayName}";

    /// <summary/>
    public virtual async Task Execute(Loadout.ReadOnly loadout, CancellationToken cancellationToken, string[]? commandLineArgs)
    {
        commandLineArgs ??= [];
        _logger.LogInformation("Starting {Name}", Name);
        
        var program = await GetGamePath(loadout);
        var primaryFile = loadout.InstallationInstance.Locations.ToAbsolutePath(_game.GetPrimaryFile(loadout.InstallationInstance));

        // On Linux, Windows .exe files can't be exec'd directly by the OS —
        // they need a Wine/Proton runner supplied by Steam or Heroic. Route to
        // the launcher regardless of whether `program` is the primary game file
        // or a modded loader (e.g. f4se_loader.exe, skse64_loader.exe). Users
        // launching a script extender on Steam Linux must configure it in Steam
        // Launch Options — Steam will then apply that when the launcher URL is
        // invoked. On Windows we keep the previous behaviour and exec the
        // loader directly since .exe files are native.
        if (OSInformation.Shared.IsLinux)
        {
            var locatorResult = loadout.InstallationInstance.LocatorResult;
            if (locatorResult.Store == GameStore.Steam)
            {
                if (!program.Equals(primaryFile))
                    _logger.LogInformation("Delegating launch to Steam (Linux). Configure `{Program}` in Steam Launch Options if needed.", program.FileName);
                await RunThroughSteam(locatorResult.StoreIdentifier, cancellationToken, commandLineArgs, BuildProcessNameSet(program, primaryFile));
                return;
            }

            if (locatorResult.Store == GameStore.GOG)
            {
                if (!program.Equals(primaryFile))
                    _logger.LogInformation("Delegating launch to Heroic (Linux). Configure `{Program}` in Heroic launch options if needed.", program.FileName);
                await RunThroughHeroic("gog", locatorResult.StoreIdentifier, cancellationToken, commandLineArgs, BuildProcessNameSet(program, primaryFile));
                return;
            }

            if (locatorResult.Store == GameStore.EGS)
            {
                // Heroic's Epic runner is "legendary"
                // (https://heroicgameslauncher.com/docs/integrations/legendary).
                if (!program.Equals(primaryFile))
                    _logger.LogInformation("Delegating launch to Heroic (Linux). Configure `{Program}` in Heroic launch options if needed.", program.FileName);
                await RunThroughHeroic("legendary", locatorResult.StoreIdentifier, cancellationToken, commandLineArgs, BuildProcessNameSet(program, primaryFile));
                return;
            }
        }

        var names = BuildProcessNameSet(program, primaryFile);

        // In the case of a preloader, we need to wait for the actual game file to exit
        // before we completely exit this routine. So get a list of all the processes with a give
        // name at the start, after the preloader finishes find any other processes with the same set of
        // names, and then we wait for those to exit.

        // In the case of something like Skyrim this means we will start with loading skse64_loader.exe then
        // notice that SkyrimSE.exe is running and wait for that to exit.

        var existing = FindMatchingProcesses(names).Select(p => p.Id).ToHashSet();
            
        if (UseShell)
        {
            _logger.LogInformation("Running {Program} through shell", program);
            await RunWithShell(cancellationToken, program);
        }
        else
        {
            _ = await RunCommand(cancellationToken, commandLineArgs, program);
        }


        var maxProcessCount = 0;
        _logger.LogInformation("Waiting for processes to exit");
        while (true)
        {
            var newProcesses = CheckForNewProcesses();
            if (newProcesses.Count == 0)
                break;
            maxProcessCount = Math.Max(maxProcessCount, newProcesses.Count);
            await Task.Delay(2000, cancellationToken);
        }
        _logger.LogInformation("All {Count} processes have exited", maxProcessCount);

        _logger.LogInformation("Finished running {Program}", program);

        HashSet<Process> CheckForNewProcesses()
        {
            // Check if the process has spawned any new processes that we need to wait for (e.g. Launcher -> Game)
            var hashSet = FindMatchingProcesses(names)
                .Where(p => !existing.Contains(p.Id))
                .ToHashSet();
            return hashSet;
        }
    }

    private async Task<CommandResult> RunCommand(CancellationToken cancellationToken, string[] arguments, AbsolutePath program)
    {
        var command = new Command(program.ToString())
            .WithArguments(arguments)
            .WithWorkingDirectory(program.Parent.ToString());

        var result = await _processRunner.RunAsync(command, cancellationToken: cancellationToken);
        return result;
    }

    private async Task<Process> RunWithShell(CancellationToken cancellationToken, AbsolutePath program)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = program.ToString(),
                WorkingDirectory = program.Parent.ToString(),
                UseShellExecute = true,
                CreateNoWindow = false,
            },
            EnableRaisingEvents = true,
        };
        
        try
        {
            await _processRunner.RunAsync(process, cancellationToken);
        }
        catch (Exception e)
        {
            if (!cancellationToken.IsCancellationRequested)
                _logger.LogError(e, "While Running {Filename}", program);
        }
        
        if (process.ExitCode != 0)
            _logger.LogWarning("Application closed with a non-zero exit code ({ExitCode}): {Application}", process.ExitCode, program);
        return process;
    }

    private async Task RunThroughSteam(string appId, CancellationToken cancellationToken, string[] commandLineArgs, HashSet<string> processNames)
    {
        if (!OSInformation.Shared.IsLinux) OSInformation.Shared.ThrowUnsupported();

        var timeout = TimeSpan.FromMinutes(5);

        var existingGameProcesses = FindMatchingProcesses(processNames).Select(x => x.Id).ToHashSet();

        // Build the Steam URL with optional command line arguments
        // https://developer.valvesoftware.com/wiki/Steam_browser_protocol
        var steamUrl = $"steam://run/{appId}";
        if (commandLineArgs is { Length: > 0 })
        {
            var encodedArgs = commandLineArgs
                .Select(Uri.EscapeDataString)
                .Aggregate((a, b) => $"{a} {b}");
            steamUrl += $"//{encodedArgs}/";
        }

        _osInterop.OpenUri(new Uri(steamUrl));

        var steam = await WaitForProcessToStart("steam", timeout, existingProcesses: null, cancellationToken);
        if (steam is null) return;

        // Wait for the actual game process to appear. Steam spawns transient
        // wrappers (`reaper`, `srt-bwrap`, `pv-adverb`, `python3` for Proton…)
        // that only live a few seconds before re-exec'ing into the real game
        // binary — tracking reaper directly makes NMA think the game exited
        // right after launch. Wait on the game exe name instead, which stays
        // for the whole session.
        var gameProcess = await WaitForFirstMatchingProcessAsTask(processNames, timeout, existingGameProcesses, cancellationToken);
        if (gameProcess is null)
        {
            _logger.LogWarning("Game process matching `{Names}` did not appear within `{Timeout:g}` after launching Steam app `{AppId}`.",
                string.Join(",", processNames), timeout, appId);
            return;
        }

        _logger.LogInformation("Steam launched `{ProcessName}` (pid {Pid}); waiting for exit", gameProcess.ProcessName, gameProcess.Id);
        // Bethesda titles cascade through several short-lived helpers before
        // settling on the real game process (Fallout4Launcher.exe → f4se_loader
        // → Fallout4.exe, or similar). Polling one pid would end the session
        // as soon as the first helper exits. Instead track the set of game-
        // named processes and consider the game running while any of them is
        // alive — with a grace period so the launcher→game transition doesn't
        // read as "exited".
        await PollUntilNoMatchingProcessAsync(processNames, existingGameProcesses, cancellationToken);
    }

    private static async Task PollUntilNoMatchingProcessAsync(HashSet<string> processNames, HashSet<int> existingProcesses, CancellationToken cancellationToken)
    {
        // 15 seconds of empty polls before we conclude the game is really gone.
        // Enough to bridge Fallout4Launcher.exe → Fallout4.exe on slower disks
        // without falsely detecting exit.
        var graceThreshold = TimeSpan.FromSeconds(15);
        var pollInterval = TimeSpan.FromSeconds(2);
        DateTime? firstEmptyAt = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var anyAlive = FindMatchingProcesses(processNames).Any(p => !existingProcesses.Contains(p.Id));
            if (anyAlive)
            {
                firstEmptyAt = null;
            }
            else
            {
                firstEmptyAt ??= DateTime.UtcNow;
                if (DateTime.UtcNow - firstEmptyAt.Value >= graceThreshold) return;
            }
            try { await Task.Delay(pollInterval, cancellationToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<Process?> WaitForFirstMatchingProcessAsTask(HashSet<string> processNames, TimeSpan timeout, HashSet<int> existingProcesses, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            var candidate = FindMatchingProcesses(processNames).FirstOrDefault(p => !existingProcesses.Contains(p.Id));
            if (candidate is not null) return candidate;
            try { await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken); }
            catch (OperationCanceledException) { return null; }
        }
        return null;
    }

    private async Task RunThroughHeroic(string type, string productId, CancellationToken cancellationToken, string[] commandLineArgs, HashSet<string> processNames)
    {
        Debug.Assert(OSInformation.Shared.IsLinux);

        var heroicUrl = $"heroic://launch?appName={productId}&runner={type}";
        if (commandLineArgs is { Length: > 0 })
        {
            var encodedArgs = commandLineArgs
                .Select(Uri.EscapeDataString)
                .Select(arg => $"arg={arg}")
                .Aggregate((a, b) => $"{a}&{b}");
            heroicUrl += $"&{encodedArgs}";
        }

        // Snapshot processes already matching the game name BEFORE launching,
        // so re-launches or stale wine processes don't get mistaken for the
        // new game instance.
        var existingProcessIds = FindMatchingProcesses(processNames).Select(p => p.Id).ToHashSet();

        _osInterop.OpenUri(new Uri(heroicUrl));

        // Heroic launches the game asynchronously via Proton/Wine. The job
        // must stay alive while the game runs so GameRunningTracker keeps the
        // launch button in its "running" state and the UI knows the session
        // is ongoing.
        var startupTimeout = TimeSpan.FromMinutes(3);
        var startupDeadline = DateTime.UtcNow + startupTimeout;
        Process? gameProcess = null;
        while (DateTime.UtcNow < startupDeadline && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            gameProcess = FindMatchingProcesses(processNames)
                .FirstOrDefault(p => !existingProcessIds.Contains(p.Id));
            if (gameProcess is not null) break;
        }

        if (gameProcess is null)
        {
            _logger.LogWarning("Game process matching `{Names}` did not start within `{Timeout:g}` after Heroic launch for `{ProductId}`",
                string.Join(",", processNames), startupTimeout, productId);
            return;
        }

        _logger.LogInformation("Heroic launched `{ProcessName}` (pid {Pid}); waiting for exit", gameProcess.ProcessName, gameProcess.Id);
        // Same rationale as Steam: track the game name set to survive launcher
        // → game process transitions instead of pinning a single pid.
        await PollUntilNoMatchingProcessAsync(processNames, existingProcessIds, cancellationToken);
    }

    private static HashSet<string> BuildProcessNameSet(AbsolutePath program, AbsolutePath primaryFile)
    {
        var names = new HashSet<string>
        {
            program.FileName,
            program.GetFileNameWithoutExtension(),
            primaryFile.FileName,
            primaryFile.GetFileNameWithoutExtension(),
        };

        // Linux truncates kernel-reported process names to 15 chars (TASK_COMM_LEN),
        // which is what .NET's Process.ProcessName returns. Add truncated variants
        // so wine-launched executables like "Cyberpunk2077.exe" still match as
        // "Cyberpunk2077.e".
        foreach (var name in names.ToArray())
            if (name.Length > 15) names.Add(name[..15]);

        return names;
    }

    private async ValueTask<Process?> WaitForProcessToStart(
        string processName,
        TimeSpan timeout,
        HashSet<int>? existingProcesses,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Waiting for process `{ProcessName}` to start within `{Timeout:g}` second(s)", processName, timeout);

        try
        {
            var start = DateTime.UtcNow;
            while (!cancellationToken.IsCancellationRequested && start + timeout > DateTime.UtcNow)
            {
                var processes = Process.GetProcessesByName(processName);
                var target = existingProcesses is not null
                    ? processes.FirstOrDefault(x => !existingProcesses.Contains(x.Id))
                    : processes.FirstOrDefault();

                if (target is not null) return target;

                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            }

            _logger.LogWarning("Process `{ProcessName}` failed to start within `{Timeout:g}` second(s)", processName, timeout);
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception while waiting for process `{Process}` to start", processName);
            return null;
        }
    }

    private static HashSet<Process> FindMatchingProcesses(HashSet<string> names)
    {
        return Process.GetProcesses()
            .Where(p => names.Contains(p.ProcessName))
            .ToHashSet();
    }

    /// <summary>
    /// Returns the path to the main executable file for the game.
    /// </summary>
    /// <param name="loadout"></param>
    /// <param name="applyPlan"></param>
    /// <returns></returns>
    protected virtual ValueTask<AbsolutePath> GetGamePath(Loadout.ReadOnly loadout)
    {
        var primaryFile = loadout.InstallationInstance.Locations.ToAbsolutePath(_game.GetPrimaryFile(loadout.InstallationInstance));
        return new ValueTask<AbsolutePath>(primaryFile);
    }

    /// <inheritdoc />
    public IJobTask<ITool, Unit> StartJob(Loadout.ReadOnly loadout, IJobMonitor monitor, CancellationToken cancellationToken)
    {
        return monitor.Begin<ITool, Unit>(this, async _ =>
        {
            await Execute(loadout, cancellationToken, []);
            return Unit.Default;
        });
    }
}
