using System.Text.Json;
using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.TextCommands;
using DSharpPlus.Commands.Processors.TextCommands.Parsing;
using DSharpPlus.Voice;
using Gaida.Bot;
using Gaida.Bot.Player;
using Gaida.Core;
using Gaida.Platforms.MusicDatabase;
using Gaida.Platforms.Spotify;
using Gaida.Platforms.YouTube;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

var defaultSerializer = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

var loggerConfiguration = new LoggerConfiguration()
    .WriteTo.Console();

var logger = loggerConfiguration.CreateLogger();

BotParametersConfiguration[]? botsConfig;
var botInlineConfiguration = Environment.GetEnvironmentVariable("BOT_CONFIGURATION");
var botConfigurationLocation = Environment.GetEnvironmentVariable("CONFIGURATION_LOCATION") ?? ".env.json";

if (botInlineConfiguration is not null)
{
    botsConfig = JsonSerializer.Deserialize<BotParametersConfiguration[]>(botInlineConfiguration, defaultSerializer);
    if (botsConfig is null)
    {
        logger.Fatal("Failed to deserialize BotParametersConfiguration from inline configuration.");
        return;
    }
}
else if (!string.IsNullOrWhiteSpace(botConfigurationLocation))
{
    var file = File.ReadAllText(botConfigurationLocation);
    botsConfig = JsonSerializer.Deserialize<BotParametersConfiguration[]>(file, defaultSerializer);
    if (botsConfig is null)
    {
        logger.Fatal("Failed to deserialize BotParametersConfiguration from file.");
        return;
    }
}
else
{
    logger.Fatal(
        "Bot configuration location is missing. Please set the CONFIGURATION_LOCATION or BOT_CONFIGURATION environment variables.");
    return;
}

var serviceCollection = new ServiceCollection();
var configurationBuilder = new ConfigurationBuilder();
var configuration = configurationBuilder.Build();

serviceCollection.AddSingleton(logger);
serviceCollection.AddSingleton(configuration);
serviceCollection.AddSingleton(serviceProvider =>
{
    var manager = new AudioManager(serviceProvider.GetRequiredService<ILogger>());
    manager.RegisterPlatform<MusicDatabase>();
    manager.RegisterPlatform<YouTube>();
    manager.RegisterPlatform<Spotify>();

    manager.Initialize();
    return manager;
});
serviceCollection.AddSingleton<PlayerController>();

var applicationAssembly = typeof(Program).Assembly;

var tasks = botsConfig.Select(config => Task.Run(async () =>
{
    if (config.Token is null)
    {
        logger.Fatal("Token is missing from configuration for {Name}.", config.Name);
        return;
    }

    var builder = DiscordClientBuilder
        .CreateDefault(config.Token,
            DiscordIntents.AllUnprivileged | DiscordIntents.MessageContents | TextCommandProcessor.RequiredIntents,
            serviceCollection)
        .UseCommands((_, commandsExtension) =>
        {
            commandsExtension.AddProcessor(new TextCommandProcessor
            {
                Configuration = new TextCommandConfiguration
                {
                    PrefixResolver = new DefaultPrefixResolver(true, config.Prefixes).ResolvePrefixAsync
                }
            });
            commandsExtension.AddProcessor<SlashCommandProcessor>();
            commandsExtension.AddCommands(applicationAssembly);
        })
        .UseVoice();

    var client = builder.Build();

    await client.ConnectAsync();
    await Task.Delay(-1);
})).ToArray();
Task.WaitAll(tasks);