using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using Gaida.Bot.Player;
using JetBrains.Annotations;

namespace Gaida.Bot.Commands;

[UsedImplicitly]
public class PlayerCommands(PlayerController playerController)
{
    [Command("play")]
    [SlashCommandTypes(DiscordApplicationCommandType.SlashCommand)]
    public async Task Play(CommandContext ctx)
    {
        
    }
}