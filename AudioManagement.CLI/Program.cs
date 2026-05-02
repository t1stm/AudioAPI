using System.Text.Json;
using AudioManagement;
using AudioManagement.Platforms.MusicDatabase;
using AudioManagement.Platforms.YouTube;
using AudioManagement.Streams;
using Result.Objects;

var audioManager = new AudioManager();
audioManager.Initialize();

audioManager.RegisterPlatform<YouTube>();
audioManager.RegisterPlatform<MusicDatabase>();

// https://www.youtube.com/watch?v=dQw4w9WgXcQ
var found = await audioManager.SearchID("yt://dQw4w9WgXcQ");
if (found == Status.Error)
{
    Console.WriteLine("Status: Error");
    return;
}

var result = found.GetOk();
Console.WriteLine("Status: OK");

Console.WriteLine(JsonSerializer.Serialize(result));

var downloadAttempt = await result.TryGetContentData();
if (downloadAttempt == Status.Error)
{
    Console.WriteLine("Download: Error");
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

Console.WriteLine($"Total: {total}");