using System.Text.Json;
using Gaida.Core;
using Gaida.Platforms.MusicDatabase;
using Gaida.Platforms.YouTube;
using Gaida.Core.Streams;
using Result.Objects;
using Serilog;
using Serilog.Core;

var loggerConfiguration = new LoggerConfiguration()
    .WriteTo.Console();

var logger = loggerConfiguration.CreateLogger();
var audioManager = new AudioManager(logger);
audioManager.Initialize();

audioManager.RegisterPlatform<YouTube>();
audioManager.RegisterPlatform<MusicDatabase>();

// https://www.youtube.com/watch?v=dQw4w9WgXcQ
var found = await audioManager.SearchID("yt://dQw4w9WgXcQ");
if (found == Status.Error)
{
    logger.Error("Status: Error");
    return;
}

var result = found.GetOk();
logger.Information("Status: OK, Result: {Result}", result.SerializeSelf());

var downloadAttempt = await result.TryGetContentData();
if (downloadAttempt == Status.Error)
{
    logger.Error("Download: Error");
    return;
}

var streamSpreader = downloadAttempt.GetOk();
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

streamSpreader.Subscribe(streamSubscriber);
await waitingSemaphore.WaitAsync();

await stream.FlushAsync();
await stream.DisposeAsync();

stream.Close();

logger.Information("Total: {Total}", total);