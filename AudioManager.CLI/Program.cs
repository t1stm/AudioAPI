using System.Text.Json;
using AudioManager.Platforms.MusicDatabase;
using AudioManager.Platforms.YouTube;
using AudioManager.Streams;
using Result.Objects;

var audio_manager = new Audio.AudioManager();
audio_manager.Initialize();

audio_manager.RegisterPlatform<YouTube>();
audio_manager.RegisterPlatform<MusicDatabase>();

// https://www.youtube.com/watch?v=dQw4w9WgXcQ
var found = await audio_manager.SearchID("yt://dQw4w9WgXcQ");
if (found == Status.Error)
{
    Console.WriteLine("Status: Error");
    return;
}

var result = found.GetOK();
Console.WriteLine("Status: OK");

Console.WriteLine(JsonSerializer.Serialize(result));

var download_attempt = await result.TryGetContentData();
if (download_attempt == Status.Error)
{
    Console.WriteLine("Download: Error");
    return;
}

var stream_spreader = download_attempt.GetOK();
var stream = File.Open("test", FileMode.Create);

var waiting_semaphore = new SemaphoreSlim(0, 1);
var total = 0;
var stream_subscriber = new StreamSubscriber
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
        waiting_semaphore.Release();
        return Task.CompletedTask;
    }
};

stream_spreader.Subscribe(stream_subscriber);
await waiting_semaphore.WaitAsync();

await stream.FlushAsync();
await stream.DisposeAsync();

stream.Close();

Console.WriteLine($"Total: {total}");