using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Games.FileHashes;
using NexusMods.App.UI.Resources;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Settings;
using NexusMods.UI.Sdk;
using NexusMods.UI.Sdk.Icons;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace NexusMods.App.UI.Controls.GameWidget;

public class GameWidgetViewModel : AViewModel<IGameWidgetViewModel>, IGameWidgetViewModel
{
    private readonly ILogger<GameWidgetViewModel> _logger;

    public GameWidgetViewModel(ILogger<GameWidgetViewModel> logger, ISettingsManager settingsManager, IFileHashesService fileHashesService)
    {
        _logger = logger;

        AddGameCommand = ReactiveCommand.Create(() => { });
        ViewGameCommand = ReactiveCommand.Create(() => { });
        RemoveAllLoadoutsCommand = ReactiveCommand.Create(() => { });

        _image = this
            .WhenAnyValue(vm => vm.Installation)
            .Where(installation => installation?.Game is not null)
            .OffUi()
            .SelectMany(LoadImage)
            .WhereNotNull()
            .ToProperty(this, vm => vm.Image, scheduler: RxApp.MainThreadScheduler);

        this.WhenActivated(disposables =>
            {
                this.WhenAnyValue(vm => vm.Installation)
                    .WhereNotNull()
                    .Select(inst => $"{inst.Game.DisplayName}")
                    .BindToVM(this, vm => vm.Name)
                    .DisposeWith(disposables);

                this.WhenAnyValue(vm => vm.Installation)
                    .WhereNotNull()
                    .SelectMany(async installation =>
                    {
                        await fileHashesService.GetFileHashesDb();
                        var locatorIds = installation.LocatorResult.LocatorIds.ToArray();
                        if (fileHashesService.TryGetVanityVersion((installation.LocatorResult.Store, locatorIds), out var vanityVersion))
                            return $"Version: {vanityVersion.Value}";
                        // Fallback: read the PE version from the primary game exe so users
                        // still see something concrete (e.g. "1.10.984.0") when the upstream
                        // hashes DB has no matching version definition.
                        var peVersion = TryReadPrimaryFileVersion(installation);
                        return peVersion is not null ? $"Version: {peVersion}" : Language.GameWidget_VersionUnknown;
                    })
                    .BindToVM(this, vm => vm.Version)
                    .DisposeWith(disposables);

                this.WhenAnyValue(vm => vm.Installation)
                    .WhereNotNull()
                    .Select(inst => $"{inst.LocatorResult.Store.Value}")
                    .BindToVM(this, vm => vm.Store)
                    .DisposeWith(disposables);

                this.WhenAnyValue(vm => vm.Installation)
                    .WhereNotNull()
                    .Select(inst => MapGameStoreToIcon(inst.LocatorResult.Store))
                    .BindToVM(this, vm => vm.GameStoreIcon)
                    .DisposeWith(disposables);
                
                IsManagedObservable
                    .Select(v => v ? GameWidgetState.ManagedGame : GameWidgetState.DetectedGame)
                    .OnUI()
                    .BindToVM(this, vm => vm.State)
                    .DisposeWith(disposables);

                _image.DisposeWith(disposables);
            }
        );
    }

    private async Task<Bitmap?> LoadImage(GameInstallation? source)
    {
        if (source is null) return null;

        try
        {
            var stream = await source.Game.TileImage.GetStreamAsync();
            return Bitmap.DecodeToWidth(stream, (int) ImageSizes.GameTile.Width);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "While loading game image for {GameName}", source.Game.DisplayName);
            return null;
        }
    }

    /// <summary>
    /// Returns an <see cref="IconValue"/> for a given <see cref="GameStore"/>.
    /// </summary>
    /// <param name="store">A <see cref="GameStore"/> object</param>
    /// <returns>An <see cref="IconValue"/> icon representing the game store or a question mark icon if not found.</returns>
    internal static IconValue MapGameStoreToIcon(GameStore store)
    {
        if (store == GameStore.Steam)
            return IconValues.Steam;
        else if (store == GameStore.GOG)
            return IconValues.GOG;
        else if (store == GameStore.EGS)
            return IconValues.Epic;
        else if (store == GameStore.Origin)
            return IconValues.Ubisoft;
        else if (store == GameStore.EADesktop)
            return IconValues.EA;
        else if (store == GameStore.XboxGamePass)
            return IconValues.Xbox;

        return IconValues.Help;
    }

    [Reactive] public GameInstallation? Installation { get; set; }

    [Reactive] public string Name { get; set; } = "";
    [Reactive] public string Version { get; set; } = "";
    [Reactive] public string Store { get; set; } = "";
    public IconValue GameStoreIcon { get; set; } = new IconValue();

    private readonly ObservableAsPropertyHelper<Bitmap> _image;
    public Bitmap Image => _image.Value;

    [Reactive] public ReactiveCommand<Unit, Unit> AddGameCommand { get; set; }

    [Reactive] public ReactiveCommand<Unit, Unit> ViewGameCommand { get; set; }

    [Reactive] public ReactiveCommand<Unit, Unit> RemoveAllLoadoutsCommand { get; set; }
    
    public IObservable<bool> IsManagedObservable { get; set; } = Observable.Return(false);


    [Reactive] public GameWidgetState State { get; set; }

    private static string? TryReadPrimaryFileVersion(GameInstallation installation)
    {
        try
        {
            var primary = installation.Locations.ToAbsolutePath(installation.Game.GetPrimaryFile(installation));
            if (!primary.FileExists) return null;
            var nativePath = primary.ToNativeSeparators(NexusMods.Paths.OSInformation.Shared);

            // Prefer the framework helper — cheap and correct on Windows and for
            // most PEs on Linux/macOS via .NET's built-in resource reader.
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(nativePath);
            var raw = info.ProductVersion ?? info.FileVersion;
            if (!string.IsNullOrWhiteSpace(raw)) return raw.Trim();

            // Fallback: some Bethesda PEs (Fallout4.exe 1.11.240 for example)
            // ship VS_VERSION_INFO in a layout .NET's Linux reader returns as
            // empty strings for. Parse the resource ourselves by locating the
            // "FileVersion" UTF-16LE label and reading the null-terminated
            // string value that follows.
            return ReadPeVersionFromBytes(nativePath);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadPeVersionFromBytes(string filePath)
    {
        try
        {
            // VS_VERSION_INFO lives in the .rsrc section, typically near the
            // end of the file. Reading the last 4 MiB is enough for Bethesda
            // executables and avoids loading the whole 50+ MiB payload.
            const int tailWindow = 4 * 1024 * 1024;
            using var fs = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);
            var length = (int)Math.Min(fs.Length, tailWindow);
            fs.Seek(-length, System.IO.SeekOrigin.End);
            var bytes = new byte[length];
            var read = fs.Read(bytes, 0, length);
            if (read < length) Array.Resize(ref bytes, read);

            // Try "FileVersion" then "ProductVersion" — both are standard
            // VS_VERSION_INFO fields and one is usually enough.
            foreach (var label in new[] { "FileVersion", "ProductVersion" })
            {
                var key = System.Text.Encoding.Unicode.GetBytes(label + "\0");
                var idx = IndexOf(bytes, key);
                if (idx < 0) continue;
                var pos = idx + key.Length;
                // Skip UTF-16LE null padding between the key and the value.
                while (pos + 1 < bytes.Length && bytes[pos] == 0 && bytes[pos + 1] == 0)
                    pos += 2;
                // Read until the next UTF-16LE null terminator.
                var end = pos;
                while (end + 1 < bytes.Length && !(bytes[end] == 0 && bytes[end + 1] == 0))
                    end += 2;
                if (end <= pos) continue;
                var value = System.Text.Encoding.Unicode.GetString(bytes, pos, end - pos).Trim();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        catch
        {
            // fall through
        }
        return null;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length) return -1;
        var first = needle[0];
        var last = haystack.Length - needle.Length;
        for (var i = 0; i <= last; i++)
        {
            if (haystack[i] != first) continue;
            var match = true;
            for (var j = 1; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }
}
