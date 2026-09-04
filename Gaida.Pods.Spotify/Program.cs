using System.Runtime.CompilerServices;
using Gaida.Core.Platforms;
using Gaida.Core.Utils;
using Gaida.Pods.Spotify;
using Serilog;
using SpotifyPlatform = Gaida.Platforms.Spotify.Spotify;

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
// See Gaida.Pods.YouTube/Program.cs for the same pattern.
foreach (var key in (string[])["SPOTIFY_ID", "SPOTIFY_SECRET"])
    Environment.SetEnvironmentVariable(key, builder.Configuration[key]);

builder.Services.AddSerilog();

var app = builder.Build();

var spotify = new SpotifyPlatform(Log.Logger);
spotify.Initialize();

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

app.MapGet("/resolve", async Task<IResult> (string? id, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(id)) return Results.NotFound();

    var result = await spotify.GetByIdAsync(StripScheme(id), ct);
    var dto = result is null ? null : ResultMapper.Map(result);
    return dto is null ? Results.NotFound() : Results.Ok(dto);
});

// The route hands back the sequence itself: ASP.NET serialises an IAsyncEnumerable element by element and
// flushes as it goes. That matters here more than anywhere: Spotify pages a playlist 100 at a time and
// every track then needs its own lookup upstream, so the sooner the first one leaves, the sooner that
// lookup starts.
app.MapGet("/playlist",
    IResult (string? url, CancellationToken ct) => string.IsNullOrWhiteSpace(url)
        ? Results.Ok(Array.Empty<ResultDto>())
        : Results.Ok(Mapped(spotify.SearchPlaylist(url, ct), ct)));

// Nothing to search for, pick from, or play: Spotify hands out metadata, and every result it produces is
// resolved against a platform that has audio before it reaches a client. A keyword search already reaches
// those platforms directly, so answering one here would only add a name to resolve into the result they
// just returned. HttpPlatform reads all three 404s as "route not supported" and moves on.
app.MapGet("/search", IResult (string? q) => Results.NotFound());
app.MapGet("/random", IResult () => Results.NotFound());
app.MapGet("/content", IResult (string? id) => Results.NotFound());

app.Run();
return;

static string StripScheme(string id)
{
    var separator = id.IndexOf("://", StringComparison.Ordinal);
    return separator < 0 ? id : id[(separator + 3)..];
}

static async IAsyncEnumerable<ResultDto> Mapped(IAsyncEnumerable<PlatformResult> source,
    [EnumeratorCancellation] CancellationToken ct)
{
    await foreach (var result in source.Guarded(Log.Logger, nameof(SpotifyPlatform), ct))
        if (ResultMapper.Map(result) is { } dto)
            yield return dto;
}
