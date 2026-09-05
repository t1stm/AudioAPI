using Gaida.Admin;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using Gaida.Core.Platforms;
using Gaida.Core.Streams;
using Gaida.Core.Utils;
using Gaida.Platforms.MusicDatabase;
using Gaida.Platforms.MusicDatabase.Manager;
using Serilog;

// `dotnet run -- selftest` runs the pure-logic check below without needing a library or a
// listening host — see RunSelfCheck.
if (args.Contains("--self-check"))
{
    await RunSelfCheck();
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

// Unlike the other pods, this one owns state an operator edits: the library's names and albums.
// No-op without ADMIN_TOKEN. See ADMIN_PLAN.md.
var admin = app.MapAdmin(() => platform.Summary());

// The rows to edit. A GET, so it goes through Oko's read proxy rather than its audited action one.
admin?.MapGet("/library", IResult (string? q, int? take, MusicDatabase db) =>
    Results.Ok(db.FindForAdmin(q, take ?? 100).Select(LibraryRow)));

// Repeated `title=` / `artist=` parameters are the whole variant list, in order. A parameter that is
// absent leaves that field alone; `album=` with no value clears it. Read off the query rather than
// bound, because "not sent" and "sent empty" mean different things here and model binding gives both
// as an empty array.
admin?.MapPost("/edit-song", async Task<IResult> (string id, HttpRequest request, MusicDatabase db) =>
{
    var titles = Variants(request, "title");
    var artists = Variants(request, "artist");
    var album = request.Query.TryGetValue("album", out var value) ? value.ToString() : null;

    var (entry, error) = await db.EditAsync(id, titles, artists, album);
    if (error is not null)
        return error == "No song with that ID." ? Results.NotFound() : Results.BadRequest(new ErrorDto(error));

    return Results.Ok(LibraryRow(entry!));
});

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

// The routes hand back the sequence itself: ASP.NET serialises an IAsyncEnumerable element by element and
// flushes as it goes. Everything here is already in memory, so what it buys is the client rendering while
// a 200-track roll or a prolific artist is still being written, rather than after the last byte.
app.MapGet("/search", IResult (string? q, MusicDatabase db, CancellationToken ct) =>
    string.IsNullOrWhiteSpace(q)
        ? Results.Ok(Array.Empty<ResultDto>())
        : Results.Ok(Mapped(db.SearchKeywords(q, ct), ct)));

app.MapGet("/random", IResult (int? count, MusicDatabase db, CancellationToken ct) =>
    Results.Ok(Mapped(db.GetRandomResults(Math.Max(0, count ?? 10), ct), ct)));

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

    // Ordering moved here from Gaida.API so that end can stream the response through untouched: the whole
    // library is already in memory (MusicManager.GetArtistSongs materialises a list anyway), so sorting costs
    // nothing here where it would cost Gaida.API the entire response. The keys mirror
    // DiscoveryResultMapper's preference for the untransliterated fields, so the artist/name/id order API.md
    // documents is still what the client sees.
    var results = new List<ResultDto>();
    await foreach (var result in db.GetArtistSongs(term).WithCancellation(ct))
        if (Map(result) is { } mapped)
            results.Add(mapped);

    var sorted = results
        .OrderBy(result => result.OriginalArtist is { Length: > 0 } artist ? artist : result.Artist ?? "",
            StringComparer.OrdinalIgnoreCase)
        .ThenBy(result => result.OriginalTitle is { Length: > 0 } title ? title : result.Name ?? "",
            StringComparer.OrdinalIgnoreCase)
        .ThenBy(result => result.Id, StringComparer.Ordinal);

    return Results.Ok(sorted);
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

// ── Admin helpers ──────────────────────────────────────────────────────────────────────────────

static List<string>? Variants(HttpRequest request, string name)
{
    return request.Query.TryGetValue(name, out var values)
        ? [.. values.Select(value => value ?? string.Empty)]
        : null;
}

/// <summary>
///     One editable row. Every variant, not just the display one: the whole point of the editor is the
///     list <see cref="ResultDto" /> flattens down to a single name.
/// </summary>
static LibraryRowDto LibraryRow(MusicInfo song) => new(song.ID ?? "", [.. song.Titles], [.. song.Artists],
    song.Album, song.RelativeLocation, song.Duration.ToString("c", CultureInfo.InvariantCulture), song.CoverUrl);

static async IAsyncEnumerable<ResultDto> Mapped(IAsyncEnumerable<PlatformResult> source,
    [EnumeratorCancellation] CancellationToken ct)
{
    await foreach (var result in source.WithCancellation(ct))
        if (Map(result) is { } mapped)
            yield return mapped;
}

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
static async Task RunSelfCheck()
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

    await EditCheck(Assert);
    await BackfillCheck(Assert);

    Console.WriteLine("selftest OK");
    return;

    void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception($"selftest failed: {message}");
    }
}

/// <summary>
///     The album backfill, against a throwaway library whose one entry predates the tag being read.
///     The thing that would go wrong quietly is the ID: <c>RereadTags</c> re-rolls it, and re-rolling
///     every ID in the library to fill an album would orphan every playlist, cache key and
///     recently-played entry that holds one. The media file does not exist here, so the ffprobe read is
///     skipped and only the bookkeeping is under test.
/// </summary>
static async Task BackfillCheck(Action<bool, string> assert)
{
    var root = Path.Combine(Path.GetTempPath(), "gaida-local-backfill-" + Guid.NewGuid().ToString("n"));
    var folder = Path.Combine(root, "Duran Duran");
    Directory.CreateDirectory(folder);

    var info = Path.Combine(folder, "Info.json");
    await File.WriteAllTextAsync(info, """
        [{"ID":"ducome-un","Titles":["Come Undone"],"Artists":["Duran Duran"],
          "RelativeLocation":"Duran Duran/Duran Duran - Come Undone.mp3","Length":256000}]
        """);

    Environment.SetEnvironmentVariable("STORAGE", root, EnvironmentVariableTarget.Process);

    try
    {
        var database = new MusicDatabase(Serilog.Core.Logger.None);
        await database.InitializeAsync();

        var songs = database.FindForAdmin("Duran Duran", 10);
        assert(songs.Count == 1, "backfill: the throwaway library loaded one song");
        assert(songs[0].ID == "ducome-un", "backfill: the ID survives it -- playlists and cache keys hold it");
        assert(songs[0].Scan == MusicManager.ScanVersion, "backfill: the entry is stamped with the pass that read it");

        var saved = await File.ReadAllTextAsync(info);
        assert(saved.Contains("\"Scan\": 1"), "backfill: the stamp reached the file, so it runs once");
        assert(saved.Contains("ducome-un"), "backfill: the saved entry kept its ID");
    }
    finally
    {
        try { Directory.Delete(root, true); } catch (IOException) { /* temp dir */ }
    }
}

/// <summary>
///     The admin edit path, against a throwaway library built from one Info.json. Covers the two things
///     that would go wrong quietly: an edit must not re-roll the ID that playlists and cache keys hold,
///     and the saved file must keep the <c>$[DOMAIN]</c> placeholder rather than this host's domain.
/// </summary>
static async Task EditCheck(Action<bool, string> assert)
{
    var root = Path.Combine(Path.GetTempPath(), "gaida-local-selfcheck-" + Guid.NewGuid().ToString("n"));
    var folder = Path.Combine(root, "Queen");
    Directory.CreateDirectory(folder);

    var info = Path.Combine(folder, "Info.json");
    await File.WriteAllTextAsync(info, """
        [{"ID":"quyoure-ab","Titles":["You_re My Best Friend"],"Artists":["Queen"],
          "Album":"A Night at the Opera","CoverUrl":"$[DOMAIN]/cover.jpg",
          "RelativeLocation":"Queen/Queen - You_re My Best Friend.mp3","Length":175000}]
        """);

    Environment.SetEnvironmentVariable("STORAGE", root, EnvironmentVariableTarget.Process);
    Environment.SetEnvironmentVariable("DOMAIN", "https://music.example.com", EnvironmentVariableTarget.Process);

    try
    {
        var database = new MusicDatabase(Serilog.Core.Logger.None);
        await database.InitializeAsync();

        var before = database.FindForAdmin("Queen", 10);
        assert(before.Count == 1, "edit: the throwaway library loaded one song");
        assert(before[0].CoverUrl == "https://music.example.com/Album_Covers/cover.jpg",
            "edit: $[DOMAIN] is substituted on the way in");

        var (edited, error) = await database.EditAsync("quyoure-ab",
            ["You're My Best Friend", "You_re My Best Friend"], ["Queen", "Freddie Mercury"], "A Night at the Opera");

        assert(error is null && edited is not null, $"edit: the edit succeeded ({error})");
        assert(edited!.ID == "quyoure-ab", "edit: the ID survives an edit -- playlists and cache keys hold it");
        assert(edited.Title == "You're My Best Friend", "edit: the first title becomes the display name");
        assert(edited.Artists.Count == 2, "edit: every artist variant is kept");

        var saved = await File.ReadAllTextAsync(info);
        assert(saved.Contains("You're My Best Friend"), "edit: the new name reached the file");
        assert(saved.Contains("$[DOMAIN]"), "edit: the cover placeholder is written back, not this host's domain");
        assert(!saved.Contains("music.example.com"), "edit: no absolute domain was baked into the library");

        var (_, blank) = await database.EditAsync("quyoure-ab", [" ", ""], null, null);
        assert(blank == "A song needs at least one title.", "edit: a song cannot be left with no title");

        var (missing, notFound) = await database.EditAsync("no-such-id", null, null, "X");
        assert(missing is null && notFound == "No song with that ID.", "edit: an unknown ID is refused");

        // Album cleared by an empty value, left alone by a missing one.
        await database.EditAsync("quyoure-ab", null, null, "");
        assert(database.FindForAdmin("Queen", 10)[0].Album is null, "edit: an empty album clears it");
    }
    finally
    {
        try { Directory.Delete(root, true); } catch (IOException) { /* temp dir */ }
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

public sealed record LibraryRowDto(
    string Id,
    IReadOnlyList<string> Titles,
    IReadOnlyList<string> Artists,
    string? Album,
    string? Location,
    string Duration,
    string? CoverUrl);

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