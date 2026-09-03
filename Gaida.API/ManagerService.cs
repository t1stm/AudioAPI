using Gaida.Core;
using Gaida.Core.Platforms;
using ILogger = Serilog.ILogger;

namespace Gaida.API;

/// <summary>
///     Builds the platform list from config and hands out the <see cref="AudioManager" />. No caching, no
///     coalescing — Dunav sits upstream of every content request and owns both now.
/// </summary>
public class ManagerService
{
    public readonly AudioManager Manager;

    public ManagerService(ILogger logger, IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        Manager = new AudioManager(logger);

        foreach (var pod in configuration.GetSection("Platforms").GetChildren())
        {
            var url = pod["Url"];
            var ids = pod["Ids"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (string.IsNullOrWhiteSpace(url) || ids is not { Length: > 0 }) continue;

            var http = httpClientFactory.CreateClient($"platform-{pod.Key}");
            http.BaseAddress = new Uri(url);

            Manager.RegisterPlatform(new HttpPlatform(logger, http, ids));
        }
    }
}