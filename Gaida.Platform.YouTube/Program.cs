using System.Collections.Concurrent;
using Gaida.Core.Platforms;
using Gaida.Core.Streams;
using Gaida.Core.Utils;
using Gaida.Platform.YouTube;
using Serilog;
using YouTubePlatform = Gaida.Platforms.YouTube.YouTube;

if (args.Contains("--self-check"))
{
    ClassifySelfCheck.Run();
    return;
}

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// The platform layer reads these as process environment variables; configuration supplies the defaults.
// See Gaida.API/Program.cs:18 for the same pattern.
foreach (var key in (string[])["YOUTUBE_CACHE_DB", "YOUTUBE_CACHE"])
    Environment.SetEnvironmentVariable(key, builder.Configuration[key]);

builder.Services.AddSerilog();

var app = builder.Build();

// One instance for the process lifetime: this pod owns exactly one platform. Initialize() loads the
// Info.json search cache (YouTubeSearchProviderCached.Initialize) and orders the content downloaders
// by priority (GetterLocalCache 99 > GetterYouTubeExplode 40 > GetterYtDlp 20).
var youTube = new YouTubePlatform(Log.Logger);
youTube.Initialize();

app.MapGet("/classify", (string? query) =>
{
    var result = Classify.Parse(query);
    return result.Status switch
    {
        200 => Results.Ok(new ClassifyDto(result.Kind, result.Id, null)),
        400 => Results.Json(new ClassifyDto(null, null, result.Error), statusCode: 400),
        _ => Results.NotFound()
    };
});

app.MapGet("/resolve", async (string? id, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(id)) return Results.NotFound();

    var result = await youTube.GetByIdAsync(StripScheme(id), ct);
    var dto = result is null ? null : ResultMapper.Map(result);
    return dto is null ? Results.NotFound() : Results.Ok(dto);
});

app.MapGet("/search",
    async (string? q, CancellationToken ct) => string.IsNullOrWhiteSpace(q)
        ? Results.Ok(Array.Empty<ResultDto>())
        : Results.Ok(await Collect(youTube.SearchKeywords(q, ct), ct)));

app.MapGet("/playlist",
    async (string? url, CancellationToken ct) => string.IsNullOrWhiteSpace(url)
        ? Results.Ok(Array.Empty<ResultDto>())
        : Results.Ok(await Collect(youTube.SearchPlaylist(url, ct), ct)));

app.MapGet("/random",
    async (int count, CancellationToken ct) => count < 1
        ? Results.Ok(Array.Empty<ResultDto>())
        : Results.Ok(await Collect(youTube.GetRandomResults(count, ct), ct)));

app.MapGet("/content", async (string? id, HttpContext http, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(id)) return Results.NotFound();

    var pureId = StripScheme(id);
    var result = await youTube.GetByIdAsync(pureId, ct);
    if (result is null) return Results.NotFound();

    var spreader = await result.GetContentDataAsync(ct);
    if (spreader is null) return Results.NotFound();

    http.Response.ContentType = "audio/webm";
    http.Response.Headers.Append("Content-Disposition", $"attachment; filename={pureId}.webm");

    await PumpToResponse(spreader, http.Response, ct);
    return Results.Empty;
});

app.Run();
return;

static string StripScheme(string id)
{
    var separator = id.IndexOf("://", StringComparison.Ordinal);
    return separator < 0 ? id : id[(separator + 3)..];
}

static async Task<List<ResultDto>> Collect(IAsyncEnumerable<PlatformResult> source,
    CancellationToken ct)
{
    var results = new List<ResultDto>();
    await foreach (var result in source.Guarded(Log.Logger, nameof(YouTubePlatform), ct))
    {
        var dto = ResultMapper.Map(result);
        if (dto is not null) results.Add(dto);
    }

    return results;
}

/// <summary>
///     Pumps a stream spreader into the response body until the source closes or the client leaves. Lifted from
///     Gaida.API/Controllers/Content.cs:300 (StreamToResponse) -- every platform pod needs its own copy of this
///     per SERVICE_SPLIT_PLAN.md ("StreamSpreader survives ... inside each platform pod").
/// </summary>
static async Task PumpToResponse(StreamSpreader streamSpreader, HttpResponse response,
    CancellationToken cancellationToken)
{
    var body = response.Body;
    var cache = new ConcurrentQueue<(byte[], int, int)>();
    var finished = new SemaphoreSlim(0, 1);
    var syncGate = new SemaphoreSlim(1, 1);

    var subscriber = new StreamSubscriber
    {
        WriteCall = (bytes, offset, length) =>
        {
            cache.Enqueue((bytes, offset, length));
            return Task.FromResult(cancellationToken.IsCancellationRequested ? StreamStatus.Closed : StreamStatus.Open);
        },
        SyncCall = SyncCall,
        CloseCall = async () =>
        {
            await SyncCall();
            finished.Release();
        }
    };

    await streamSpreader.SubscribeAsync(subscriber);

    try
    {
        await finished.WaitAsync(cancellationToken);
    }
    catch (OperationCanceledException)
    {
        return;
    }

    await body.FlushAsync(cancellationToken);
    return;

    async Task SyncCall()
    {
        if (cancellationToken.IsCancellationRequested) return;
        await syncGate.WaitAsync(CancellationToken.None);

        try
        {
            while (cache.TryDequeue(out var entry))
            {
                var (bytes, offset, length) = entry;
                await body.WriteAsync(bytes.AsMemory(offset, length), cancellationToken);
            }
        }
        finally
        {
            syncGate.Release();
        }
    }
}