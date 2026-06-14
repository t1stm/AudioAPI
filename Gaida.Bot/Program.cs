using Gaida.Core;
using Gaida.Platforms.MusicDatabase;
using Gaida.Platforms.Spotify;
using Gaida.Platforms.YouTube;
using Serilog;

var loggerConfiguration = new LoggerConfiguration()
    .WriteTo.Console();

var logger = loggerConfiguration.CreateLogger();
var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");

if (string.IsNullOrWhiteSpace(token))
{
    logger.Fatal("Discord token is missing. Please set the DISCORD_TOKEN environment variable.");
    return;
}

var manager = new AudioManager(logger);
manager.RegisterPlatform<MusicDatabase>();
manager.RegisterPlatform<YouTube>();
manager.RegisterPlatform<Spotify>();

manager.Initialize();

