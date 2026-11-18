using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Games;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Settings;
using NexusMods.Games.RedEngine.Cyberpunk2077;
using NexusMods.Games.RedEngine.Cyberpunk2077.Models;
using NexusMods.Games.RedEngine.Cyberpunk2077.SortOrder;
using NexusMods.Games.RedEngine.Witcher3;

namespace NexusMods.Games.RedEngine;

public static class Services
{
    public static IServiceCollection AddRedEngineGames(this IServiceCollection services)
    {
        services.AddGame<Cyberpunk2077Game>()
            .AddRedModInfoFileModel()
            .AddRedModSortOrderModel()
            .AddRedModLoadoutGroupModel()
            .AddRedModSortOrderItemModel()
            .AddRedModQueriesSql()
            .AddSingleton<RedModSortOrderVariety, RedModSortOrderVariety>()
            .AddSingleton<ITool, RunCyberpunk2077Game>()
            .AddSingleton<ITool, RedModDeployTool>()
            .AddSettings<Cyberpunk2077Settings>();
        services.AddGame<Witcher3Game>()
            .AddSingleton<ITool, RunWitcher3Game>();
        return services;
    }
}
