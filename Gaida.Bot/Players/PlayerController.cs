using Gaida.Bot.Players;
using Gaida.Core;

namespace Gaida.Bot.Player;

public class PlayerController(AudioManager manager)
{
    public AudioManager Manager { get; } = manager;
    public Dictionary<string, PlayerSession> Sessions { get; } = new();
    
    
}