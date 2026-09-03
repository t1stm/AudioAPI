using System.Text.Json;
using Gaida.Core;
using Gaida.Core.Platforms;
using Gaida.Core.Streams;
using Gaida.Platforms.MusicDatabase;
using Gaida.Platforms.YouTube;
using Serilog;

var logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

var audioManager = new AudioManager(logger);

audioManager.RegisterPlatform(new YouTube(logger));
audioManager.RegisterPlatform(new MusicDatabase(logger));

// https://www.youtube.com/watch?v=dQw4w9WgXcQ
var result = await audioManager.SearchID("yt://dQw4w9WgXcQ");
if (result is null)
{
    logger.Error("Search: not found");
    return;
}

logger.Information("Search: OK, Result: {Result}",
    JsonSerializer.Serialize(result, CustomSerializer.SerializerOptions));

var streamSpreader = await result.GetContentDataAsync();
if (streamSpreader is null)
{
    logger.Error("Download: Error");
    return;
}

var stream = File.Open("test", FileMode.Create);

var waitingSemaphore = new SemaphoreSlim(0, 1);
var total = 0;
var streamSubscriber = new StreamSubscriber
{
    WriteCall = async (bytes, offset, length) =>
    {
        total += length;

        await stream.WriteAsync(bytes.AsMemory(offset, length));
        return StreamStatus.Open;
    },
    SyncCall = () => Task.CompletedTask,
    CloseCall = () =>
    {
        waitingSemaphore.Release();
        return Task.CompletedTask;
    }
};

await streamSpreader.SubscribeAsync(streamSubscriber);
await waitingSemaphore.WaitAsync();

await stream.FlushAsync();
await stream.DisposeAsync();

logger.Information("Total: {Total}", total);