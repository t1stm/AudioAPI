using System.Collections.Concurrent;
using System.Globalization;
using Gaida.Core.Platforms;
using Gaida.Core.Streams;
using Gaida.Platforms.MusicDatabase;
using Gaida.Platforms.MusicDatabase.Manager;
using Serilog;

// `dotnet run -- selftest` runs the pure-logic check below without needing a library or a
// listening host — see RunSelfCheck.
if (args.Contains("--self-check"))
{
    RunSelfCheck();
    return;
}

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// MusicManager (and CoverExtractor beneath it) read these as process environment variables,
// not through IConfiguration — see Gaida.API/Program.cs:18 for the pattern this mirrors.
foreach (var key in (string[])["DOMAIN", "STORAGE", "ALBUM_COVERS"])
    Environment.SetEnvironmentVariable(key, builder.Configuration[key]);

builder.Services.AddSingleton(Log.Logger);

var platform = new MusicDatabase(Log.Logger);
platform.Initialize(); // kicks off MusicManager.Initialize() (library scan) in the background
builder.Services.AddSingleton(platform);

var app = builder.Build();

app.MapGet("/classify", IResult (string? query) =>
{
    var (claimed, id, error) = ClassifyAudio(query);
    if (!claimed) return Results.NotFound();
    return error is not null
        ? Results.BadRequest(new ClassifyDto(null, null, error))
        : Results.Ok(new ClassifyDto("id", id, null));
});

app.MapGet("/resolve", async Task<IResult> (string? id, MusicDatabase db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(id)) return Results.NotFound();
    var result = await db.GetByIdAsync(StripAudioPrefix(id), ct);
    var mapped = result is null ? null : Map(result);
    return mapped is null ? Results.NotFound() : Results.Ok(mapped);
});

app.MapGet("/search", async Task<IResult> (string? q, MusicDatabase db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.Ok(Array.Empty<ResultDto>());

    var results = new List<ResultDto>();
    await foreach (var result in db.SearchKeywords(q, ct).WithCancellation(ct))
        if (Map(result) is { } mapped)
            results.Add(mapped);
    return Results.Ok(results);
});

app.MapGet("/random", async Task<IResult> (int? count, MusicDatabase db, CancellationToken ct) =>
{
    var results = new List<ResultDto>();
    await foreach (var result in db.GetRandomResults(Math.Max(0, count ?? 10), ct).WithCancellation(ct))
        if (Map(result) is { } mapped)
            results.Add(mapped);
    return Results.Ok(results);
});

// This platform has no playlists — nothing here ever claims a query.
app.MapGet("/playlist", IResult (string? url) => Results.NotFound());

app.MapGet("/content", async Task<IResult> (string? id, MusicDatabase db, HttpResponse response,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(id)) return Results.NotFound();

    var result = await db.GetByIdAsync(StripAudioPrefix(id), ct);
    if (result is null) return Results.NotFound();

    var spreader = await result.GetContentDataAsync(ct);
    if (spreader is null) return Results.NotFound();

    var extension = result is MusicResult local ? Path.GetExtension(local.Path) : string.Empty;
    response.ContentType = ContentTypeFor(extension);
    response.Headers.Append("Content-Disposition", $"attachment; filename={FileId(id)}{extension}");

    await PumpToResponse(spreader, response, ct);
    return Results.Empty;
});

app.MapGet("/browse", IResult (string? path, MusicDatabase db) =>
{
    // A path nobody has is an empty folder rather than a 404: nothing here reads the disk, so
    // "unknown" and "empty" are the same answer and the client renders both the same way.
    var folder = (path ?? string.Empty).Replace('\\', '/').Trim('/');
    var (folders, files) = db.Browse(folder);

    var mappedFiles = new List<ResultDto>(files.Count);
    foreach (var file in files)
        if (Map(file) is { } mapped)
            mappedFiles.Add(mapped);

    var mappedFolders = folders.Select(child => new BrowseFolderDto(child.Name,
        folder.Length == 0 ? child.Name : $"{folder}/{child.Name}", child.Songs)).ToArray();

    return Results.Ok(new BrowseDto(folder, mappedFolders, mappedFiles));
});

app.MapGet("/artist", async Task<IResult> (string? term, MusicDatabase db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(term)) return Results.Ok(Array.Empty<ResultDto>());

    // Ordering is Gaida.API's job (it also merges in YouTube results) — this comes back unordered.
    var results = new List<ResultDto>();
    await foreach (var result in db.GetArtistSongs(term).WithCancellation(ct))
        if (Map(result) is { } mapped)
            results.Add(mapped);
    return Results.Ok(results);
});

app.MapGet("/variant", IResult (string? name, string? artist, string? duration, MusicDatabase db) =>
{
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest(new ErrorDto("A track name is required."));

    if (!TryParseDuration(duration, out var length, out var error))
        return Results.BadRequest(new ErrorDto(error!));

    var found = db.FindLocalVariant(name, artist, length);
    if (found is null) return Results.NoContent();

    var (match, result) = found.Value;
    var mapped = Map(result);
    if (mapped is null) return Results.NoContent();

    return Results.Ok(new VariantDto(MapKind(match.Kind), Math.Round(match.Score, 3),
        (int)Math.Round(match.DurationDelta.TotalSeconds), match.YouTubeTags, match.LibraryTags, mapped));
});

app.Run();
return;

// ── DTOs — the public shape of this pod. No contentUrl: the platform doesn't know the public host. ──

static ResultDto? Map(PlatformResult result)
{
    if (string.IsNullOrWhiteSpace(result.ID)) return null;
    var duration = result.Duration < TimeSpan.Zero ? TimeSpan.Zero : result.Duration;
    return new ResultDto(result.ID, result.Name, result.Artist, result.Album,
        duration.ToString("c", CultureInfo.InvariantCulture), result.ThumbnailUrl,
        result.OriginalTitle, result.OriginalArtist);
}

// ── /classify, pulled out of the route so RunSelfCheck can exercise it without a host. ──

static (bool Claimed, string? Id, string? Error) ClassifyAudio(string? query)
{
    var trimmed = query?.Trim() ?? string.Empty;
    if (!trimmed.StartsWith("audio://", StringComparison.OrdinalIgnoreCase))
        return (false, null, null);

    var id = trimmed["audio://".Length..];
    return string.IsNullOrWhiteSpace(id)
        ? (true, null, "An audio:// query must include an ID.")
        : (true, "audio://" + id, null);
}

static string StripAudioPrefix(string id)
{
    return id.StartsWith("audio://", StringComparison.OrdinalIgnoreCase) ? id["audio://".Length..] : id;
}

// ── /variant helpers, also pulled out for RunSelfCheck. ──

static bool TryParseDuration(string? duration, out TimeSpan length, out string? error)
{
    length = TimeSpan.Zero;
    error = null;
    if (string.IsNullOrWhiteSpace(duration)) return true;
    if (TimeSpan.TryParse(duration, CultureInfo.InvariantCulture, out length)) return true;

    error = "The duration must look like 00:04:32.";
    return false;
}

static string MapKind(LocalMatchKind kind)
{
    return kind switch
    {
        LocalMatchKind.Same => "same",
        LocalMatchKind.Variant => "variant",
        _ => "weak"
    };
}

// ── /content helpers. ──

/// <summary>
///     The library holds .wv, .mp3, .ogg and .flac (see MUSICDB_FORMAT_PLAN.md), plus whatever else
///     MusicManager.IsAudioBasedOnFileExtension accepts. Gaida and Dunav relay this verbatim, so a
///     wrong value here breaks playback downstream.
/// </summary>
static string ContentTypeFor(string extension)
{
    return extension.ToLowerInvariant() switch
    {
        ".flac" => "audio/flac",
        ".ogg" => "audio/ogg",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".mka" => "audio/mka",
        ".adts" => "audio/aac",
        ".wma" => "audio/x-ms-wma",
        ".wv" => "audio/x-wavpack",
        _ => "application/octet-stream"
    };
}

/// <summary>The ID without its platform protocol, safe to put in a header — mirrors Gaida.API/Controllers/Content.cs's FileId.</summary>
static string FileId(string id)
{
    var separator = id.IndexOf("://", StringComparison.Ordinal);
    var value = separator >= 0 ? id[(separator + 3)..] : id;
    return Uri.EscapeDataString(value);
}

/// <summary>
///     Pumps a stream spreader into the response body until the source closes or the client leaves.
///     The source (MusicGetter, a download) may still be writing while this drains it -- the reader follows
///     the body as it grows rather than stopping at whatever had arrived when it opened.
/// </summary>
static async Task PumpToResponse(StreamSpreader spreader, HttpResponse response, CancellationToken ct)
{
    await using var reader = spreader.OpenRead();
    await reader.CopyToAsync(response.Body, ct);
}

// ── The one runnable check: `dotnet run --project Gaida.Pods.MusicDatabase -- selftest`. ──
// Exercises the pure /classify and /variant logic above without needing a library on disk or a
// listening host. Throws (nonzero exit) the moment any assertion fails.
static void RunSelfCheck()
{
    var claimed = ClassifyAudio("audio://abc123");
    Assert(claimed is (true, "audio://abc123", null), "classify: plain id");

    var mixedCase = ClassifyAudio("  AUDIO://Foo  ");
    Assert(mixedCase is (true, "audio://Foo", null), "classify: trims and lowercases only the prefix");

    var empty = ClassifyAudio("audio://");
    Assert(empty is (true, null, "An audio:// query must include an ID."), "classify: missing id is a 400");

    var notMine = ClassifyAudio("yt://dQw4w9WgXcQ");
    Assert(notMine is (false, null, null), "classify: another platform's scheme is unclaimed");

    Assert(ClassifyAudio(null) is (false, null, null), "classify: null query is unclaimed");

    Assert(MapKind(LocalMatchKind.Same) == "same", "variant: Same maps to \"same\"");
    Assert(MapKind(LocalMatchKind.Variant) == "variant", "variant: Variant maps to \"variant\"");
    Assert(MapKind(LocalMatchKind.Weak) == "weak", "variant: Weak maps to \"weak\"");

    Assert(TryParseDuration(null, out var zero, out var noError) && zero == TimeSpan.Zero && noError is null,
        "variant: missing duration parses as zero, not an error");
    Assert(TryParseDuration("00:04:32", out var parsed, out _) &&
           parsed == new TimeSpan(0, 4, 32), "variant: a valid duration round-trips");
    Assert(!TryParseDuration("not-a-duration", out _, out var badError) &&
           badError == "The duration must look like 00:04:32.", "variant: bad duration keeps the exact message");

    Console.WriteLine("selftest OK");
    return;

    void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception($"selftest failed: {message}");
    }
}

// ── DTO shapes. Deliberately not shared with Gaida.API/Contracts/DiscoveryContracts.cs: this pod
// doesn't know the public host (no contentUrl), and Gaida.API adds fields (contentUrl) this must not. ──

public sealed record ResultDto(
    string Id,
    string? Name,
    string? Artist,
    string? Album,
    string Duration,
    string? ThumbnailUrl,
    string? OriginalTitle,
    string? OriginalArtist);

public sealed record ClassifyDto(string? Kind, string? Id, string? Error);

public sealed record ErrorDto(string Error);

public sealed record BrowseFolderDto(string Name, string Path, int Songs);

public sealed record BrowseDto(string Path, IReadOnlyList<BrowseFolderDto> Folders, IReadOnlyList<ResultDto> Files);

public sealed record VariantDto(
    string Match,
    double Score,
    int DurationDeltaSeconds,
    IReadOnlyList<string> YouTubeTags,
    IReadOnlyList<string> LibraryTags,
    ResultDto Result);