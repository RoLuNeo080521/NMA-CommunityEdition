using NexusMods.Abstractions.Games;

namespace NexusMods.Games.RedEngine.Witcher3;

public class RunWitcher3Game(IServiceProvider provider, Witcher3Game game) : RunGameTool<Witcher3Game>(provider, game);
