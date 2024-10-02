using AudioManager.Platforms.Errors;
using AudioManager.Streams;
using Result;
using Result.Objects;

namespace AudioManager.Platforms;

public abstract class PlatformResult
{
    public required string ID;
    public required IReadOnlyList<ContentDownloader> Downloaders;
    
    public string? Name { get; protected set; }
    public string? Artist { get; protected set; }
    public string? Album { get; protected set; }
    public TimeSpan Duration { get; protected set; }

    public virtual async Task<Result<StreamSpreader, DownloadError>> TryGetData(CancellationToken token = default)
    {
        foreach (var downloader in Downloaders)
        {
            var result = await downloader.TryGetContentData(this, token);
            if (result == Status.OK) return result;
        }
        
        return Result<StreamSpreader, DownloadError>.Error(default);
    }
}