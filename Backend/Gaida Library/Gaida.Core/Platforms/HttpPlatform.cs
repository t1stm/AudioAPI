using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Gaida.Core.Platforms.Optional.Supports;
using Gaida.Core.Streams;
using Gaida.Core.Utils;
using Serilog;

namespace Gaida.Core.Platforms;

/// <summary>
///     A platform pod reached over HTTP instead of in-process. Every route answers 404 when the pod does not
///     support it, so every method here degrades to "nothing found" rather than throwing.
/// </summary>
public sealed class HttpPlatform : Platform, ISupportsSearch, ISupportsPlaylist, ISupportsRandomResults
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public HttpPlatform(ILogger logger, HttpClient http, IReadOnlyCollection<string> ids) : base(logger)
    {
        _http = http;
        var getter = new HttpGetter(logger, http);

        SearchIDIdentifiers = [.. ids];
        SearchPlaylistIdentifiers = [.. ids];
        SearchProviders = [];
        ContentDownloaders = [getter];
    }

    protected override HashSet<string> SearchIDIdentifiers { get; }
    protected override HashSet<string> SearchPlaylistIdentifiers { get; }
    protected override List<SearchProvider> SearchProviders { get; set; }
    protected override List<ContentGetter> ContentDownloaders { get; set; }

    public IAsyncEnumerable<PlatformResult> SearchPlaylist(string playlist,
        CancellationToken cancellationToken = default)
    {
        return FetchList($"/playlist?url={Uri.EscapeDataString(playlist)}", cancellationToken);
    }

    public IAsyncEnumerable<PlatformResult> GetRandomResults(int count, CancellationToken cancellationToken = default)
    {
        return FetchList($"/random?count={count}", cancellationToken);
    }

    public IAsyncEnumerable<PlatformResult> SearchKeywords(string keywords,
        CancellationToken cancellationToken = default)
    {
        return FetchList($"/search?q={Uri.EscapeDataString(keywords)}", cancellationToken);
    }

    /// <summary>Bypasses the search-provider chain: a pod has exactly one opinion on an ID, over one route.</summary>
    public override async Task<PlatformResult?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var dto = await GetAsync<PodResultDto>($"/resolve?id={Uri.EscapeDataString(id)}", cancellationToken);
        return dto is null ? null : ToResult(dto);
    }

    /// <summary>Local-pod-only: one level of the library's folder tree.</summary>
    public async Task<(IReadOnlyList<(string Name, int Songs)> Folders, IReadOnlyList<PlatformResult> Files)>
        BrowseAsync(
            string path, CancellationToken cancellationToken = default)
    {
        var dto = await GetAsync<PodBrowseDto>($"/browse?path={Uri.EscapeDataString(path)}", cancellationToken);
        if (dto is null) return ([], []);

        var folders = (dto.Folders ?? []).Select(f => (f.Name ?? "", f.Songs)).ToArray();
        var files = (dto.Files ?? []).Select(ToResult).ToArray();
        return (folders, files);
    }

    /// <summary>Local-pod-only: every track by an artist.</summary>
    public IAsyncEnumerable<PlatformResult> ArtistAsync(string term, CancellationToken cancellationToken = default)
    {
        return FetchList($"/artist?term={Uri.EscapeDataString(term)}", cancellationToken);
    }

    /// <summary>
    ///     Local-pod-only: whether the library already has the track a YouTube result names. Null when not worth
    ///     offering.
    /// </summary>
    public async Task<PodVariantDto?> VariantAsync(string name, string? artist, TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var query = $"/variant?name={Uri.EscapeDataString(name)}&artist={Uri.EscapeDataString(artist ?? "")}" +
                    $"&duration={Uri.EscapeDataString(duration.ToString("c", CultureInfo.InvariantCulture))}";
        return await GetAsync<PodVariantDto>(query, cancellationToken);
    }

    /// <summary>
    ///     The pod's raw <c>/content?id=</c> response — headers and a pull stream — for a direct copy into the
    ///     response body or ffmpeg's stdin. The caller disposes it. <c>null</c> on 404 or any other failure; this
    ///     is deliberately not routed through <see cref="HttpGetter" />'s <see cref="StreamSpreader" />, since a
    ///     spreader cannot be read from and Gaida.API never fans this out to more than one consumer.
    /// </summary>
    /// <param name="format">
    ///     What the caller intends to do with the bytes, when that changes what is worth fetching — today only
    ///     <c>"flac"</c>, which asks a pod that has a choice of source qualities for its lossless one. A hint,
    ///     not a demand: a pod that does not know the parameter ignores it and answers as it always did, so
    ///     nothing downstream may assume the response is in the format it asked for.
    /// </param>
    public async Task<HttpResponseMessage?> GetContentResponseAsync(string id, string? format = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"/content?id={Uri.EscapeDataString(id)}";
        if (!string.IsNullOrEmpty(format)) query += $"&format={Uri.EscapeDataString(format)}";

        try
        {
            var response = await _http.GetAsync(query, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.IsSuccessStatusCode) return response;

            response.Dispose();
            return null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Logger.Warning(e, "Content request failed for {Id}", id);
            return null;
        }
    }

    /// <returns>The pod's classification, or <c>null</c> when it does not claim the query (404, or unreachable).</returns>
    public async Task<ClassifyClaim?> ClassifyAsync(string query, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync($"/classify?query={Uri.EscapeDataString(query)}", cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Logger.Warning(e, "Classify request failed for {Query}", query);
            return null;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound) return null;

            ClassifyDto? dto;
            try
            {
                dto = await response.Content.ReadFromJsonAsync<ClassifyDto>(jsonOptions, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Logger.Warning(e, "Classify response for {Query} was not valid JSON", query);
                return null;
            }

            if (dto is null) return null;

            if (!response.IsSuccessStatusCode)
                return new ClassifyClaim(QueryType.Keywords, query, dto.Error ?? "The query is not supported.");

            return new ClassifyClaim(dto.Kind == "playlist" ? QueryType.Playlist : QueryType.ID,
                dto.Id ?? query, null);
        }
    }

    /// <summary>
    ///     A pod DTO as this platform's result, downloaders attached. Public because a sub-resource can come
    ///     back outside a list route — <c>/variant</c>'s matched track, which the resolver plays.
    /// </summary>
    public HttpResult ToResult(PodResultDto dto)
    {
        return new HttpResult
        {
            ID = dto.Id ?? "",
            Name = dto.Name,
            Artist = dto.Artist,
            Album = dto.Album,
            Duration = TimeSpan.TryParse(dto.Duration, CultureInfo.InvariantCulture, out var duration)
                ? duration
                : TimeSpan.Zero,
            ThumbnailUrl = dto.ThumbnailUrl,
            OriginalTitle = dto.OriginalTitle,
            OriginalArtist = dto.OriginalArtist,
            Downloaders = ContentDownloaders
        };
    }

    /// <summary>
    ///     Reads the pod's array as it arrives rather than after its last byte, so a slow producer (a long
    ///     YouTube playlist, a per-track lookup) reaches the caller item by item.
    /// </summary>
    /// <remarks>
    ///     <see cref="HttpCompletionOption.ResponseHeadersRead" /> is not optional here: the default buffers
    ///     the whole body before <c>GetAsync</c> returns, and the async enumerator would then be reading from
    ///     memory. A <c>try</c> cannot wrap a <c>yield return</c>, so the failure handling that
    ///     <see cref="GetAsync{T}" /> does inline sits in <see cref="FetchList" /> around this instead —
    ///     same "never take the other pods down" contract, one place, every caller covered.
    /// </remarks>
    private async IAsyncEnumerable<PlatformResult> FetchListCore(string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var response =
            await _http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // A pod that does not support a route answers 404, which is "nothing found", not a failure.
        if (!response.IsSuccessStatusCode) yield break;

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (var dto in JsonSerializer.DeserializeAsyncEnumerable<PodResultDto>(body, jsonOptions,
                           cancellationToken))
            if (dto is not null)
                yield return ToResult(dto);
    }

    private IAsyncEnumerable<PlatformResult> FetchList(string path, CancellationToken cancellationToken)
    {
        return FetchListCore(path, cancellationToken).Guarded(Logger, path, cancellationToken);
    }

    /// <summary>
    ///     GETs and deserialises <paramref name="path" />, returning <c>null</c> on 404, any other failure,
    ///     or a body that does not parse — a pod that doesn't support a route must never take the others down with it.
    /// </summary>
    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        try
        {
            using var response = await _http.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<T>(jsonOptions, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Logger.Warning(e, "Request to {Path} failed", path);
            return null;
        }
    }
}

/// <summary>A search/resolve/playlist/random result as a pod hands it over — no <c>contentUrl</c>, no public host.</summary>
public sealed class PodResultDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? Duration { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? OriginalTitle { get; set; }
    public string? OriginalArtist { get; set; }
}

public sealed class PodBrowseFolderDto
{
    public string? Name { get; set; }
    public int Songs { get; set; }
}

public sealed class PodBrowseDto
{
    public List<PodBrowseFolderDto>? Folders { get; set; }
    public List<PodResultDto>? Files { get; set; }
}

public sealed class PodVariantDto
{
    public string? Match { get; set; }
    public double Score { get; set; }
    public int DurationDeltaSeconds { get; set; }
    public List<string>? YouTubeTags { get; set; }
    public List<string>? LibraryTags { get; set; }
    public PodResultDto? Result { get; set; }
}

internal sealed class ClassifyDto
{
    public string? Kind { get; set; }
    public string? Id { get; set; }
    public string? Error { get; set; }
}

/// <summary>
///     One pod's answer to <c>/classify</c>: what it thinks the query is, or its own error for a claimed-but-invalid
///     one.
/// </summary>
public readonly record struct ClassifyClaim(QueryType Kind, string Query, string? Error);

public sealed class HttpResult : PlatformResult
{
    public override string GetDownloadUrl()
    {
        return ID;
    }
}

/// <summary>Streams <c>/content?id=</c> into a spreader, mirroring every other <see cref="ContentGetter" />.</summary>
public sealed class HttpGetter(ILogger logger, HttpClient http) : ContentGetter(logger)
{
    public override int Priority => 0;

    public override async Task<StreamSpreader?> GetContentDataAsync(PlatformResult result,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync($"/content?id={Uri.EscapeDataString(result.ID)}",
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Logger.Warning(e, "Content request failed for {Id}", result.ID);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            return null;
        }

        var spreader = new StreamSpreader();
        _ = PumpAsync();
        return spreader;

        async Task PumpAsync()
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await stream.CopyToAsync(spreader, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Logger.Warning(e, "Streaming content failed for {Id}", result.ID);
            }
            finally
            {
                response.Dispose();
                await spreader.CloseAsync();
            }
        }
    }
}