using System.Text.Json;
using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.TextCommands;
using DSharpPlus.Commands.Processors.TextCommands.Parsing;
using DSharpPlus.Voice;
using Gaida.Bot;
using Gaida.Bot.Players;
using Gaida.Core;
using Gaida.Platforms.MusicDatabase;
using Gaida.Platforms.YouTube;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

var defaultSerializer = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

var loggerConfiguration = new LoggerConfiguration()
    .WriteTo.Console();

var logger = loggerConfiguration.CreateLogger();

var botInlineConfiguration = Environment.GetEnvironmentVariable("BOT_CONFIGURATION");
var botConfigurationLocation = Environment.GetEnvironmentVariable("CONFIGURATION_LOCATION") ?? ".env.json";

var botsConfig = JsonSerializer.Deserialize<BotParametersConfiguration[]>(
    botInlineConfiguration ?? File.ReadAllText(botConfigurationLocation), defaultSerializer);

if (botsConfig is null)
{
    logger.Fatal("Failed to read the bot configuration from {Source}.",
        botInlineConfiguration is not null ? "BOT_CONFIGURATION" : botConfigurationLocation);
    return;
}

var serviceCollection = new ServiceCollection();

serviceCollection.AddSingleton(logger);
serviceCollection.AddSingleton(serviceProvider =>
{
    var serviceLogger = serviceProvider.GetRequiredService<ILogger>();
    var manager = new AudioManager(serviceLogger);

    manager.RegisterPlatform(new MusicDatabase(serviceLogger));
    manager.RegisterPlatform(new YouTube(serviceLogger));

    return manager;
});
serviceCollection.AddSingleton<PlayerController>();

var applicationAssembly = typeof(Program).Assembly;

var tasks = botsConfig.Select(config =>
    Task.Run(async () =>
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