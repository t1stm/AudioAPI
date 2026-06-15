using DSharpPlus.Entities;
using DSharpPlus.Voice;

namespace Gaida.Bot.Players;

public class PlayerSession
{
    public required DiscordGuild Guild { get; init; }
    public required DiscordChannel Channel { get; init; }
    public required VoiceConnection Connection { get; init; }

    public required Player Player { get; init; }
}