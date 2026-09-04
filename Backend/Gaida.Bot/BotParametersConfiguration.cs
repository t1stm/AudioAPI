namespace Gaida.Bot;

public class BotParametersConfiguration
{
    /// <summary>
    /// The name of the bot that will be used in logging.
    /// </summary>
    public string? Name { get; set; }
    
    /// <summary>
    /// The Discord Bot token for the current bot.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Message prefixes the bot responds to.
    /// </summary>
    public string[] Prefixes { get; set; } = [];
}