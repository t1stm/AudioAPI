using AudioManager.Platforms.Errors;
using AudioManager.Streams;
using Result;

namespace AudioManager.Platforms.MusicDatabase.Getters;

public class MusicGetter : ContentGetter
{
    public override int Priority => 99;
    
    public override Task<Result<StreamSpreader, DownloadError>> TryGetContentData(
        PlatformResult result, CancellationToken cancellation_token)
    {
        if (result is not MusicResult local_result) 
            return Task.FromResult(Result<StreamSpreader, DownloadError>.Error(DownloadError.WrongType));
        
        if (!File.Exists(local_result.Path)) return Task.FromResult(Result<StreamSpreader, DownloadError>.Error(
            DownloadError.FileReadFailure));
            
        var stream_spreader = new StreamSpreader();
        _ = Task.Run(async () =>
        {
            await using var stream = File.Open(local_result.Path, FileMode.Open, FileAccess.Read);
            await stream.CopyToAsync(stream_spreader, cancellation_token);
            await stream_spreader.CloseAsync();
        }, cancellation_token);
        
        return Task.FromResult(Result<StreamSpreader, DownloadError>.Success(stream_spreader));
    }
}