using Gaida.Core;

namespace Gaida.Bot.Players;

public class PlayerController(AudioManager manager)
{
    public AudioManager Manager { get; } = manager;
    public Dictionary<string, PlayerSession> Sessions { get; } = new();
    
    
}