using System.Text.Json;
using System.Text.Json.Serialization;
using AudioManager.Platforms.Errors;
using AudioManager.Streams;
using Result;
using Result.Objects;

namespace AudioManager.Platforms;

public abstract class PlatformResult
{
    [JsonInclude]
    public required string ID;
    [JsonIgnore]
    public required IReadOnlyList<ContentGetter> Downloaders = [];
    [JsonInclude]
    public string? Name { get; set; }
    [JsonInclude]
    public string? Artist { get; set; }
    [JsonInclude]
    public string? Album { get; set; }
    [JsonInclude]
    public TimeSpan Duration { get; set; }
    [JsonInclude]
    public string? ThumbnailUrl { get; set; }
    public abstract string GetDownloadUrl();

    public virtual string GetPureID()
    {
        var split_id = ID.Split("://");
        var pure_id = split_id.Length > 1 ? string.Join("://", split_id[1..]) : ID;
        
        return pure_id;
    }

    public virtual async Task<Result<StreamSpreader, DownloadError>> TryGetContentData(CancellationToken token = default)
    {
        foreach (var downloader in Downloaders)
        {
            var result = await downloader.TryGetContentData(this, token);
            if (result == Status.OK) return result;
        }
        
        return Result<StreamSpreader, DownloadError>.Error(default);
    }

    public virtual string SerializeSelf()
    {
        return JsonSerializer.Serialize(this, CustomSerializer.SerializerOptions);
    }
}