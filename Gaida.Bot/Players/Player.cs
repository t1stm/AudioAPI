using DSharpPlus.Entities;
using DSharpPlus.Voice;
using Gaida.Core.FFmpeg;

namespace Gaida.Bot.Players;

public class Player(VoiceConnection connection, DiscordMessage message)
{
    protected VoiceConnection Connection { get; } = connection;
    protected DiscordMessage StatusbarMessage { get; } = message;
    protected CancellationTokenSource CancellationTokenSource { get; } = new();
    
    public PlayerQueue Queue { get; } = new() { OnCurrentChange = _ => { /* TODO */ } };

    public async Task Play()
    {
        var writer = Connection.CreateAudioWriter(AudioFormat.Float32LE48KHzStereoPCM);

        // ponytail: unfinished — nothing ever fed the old encoder either, so there is no behaviour
        // to preserve here, only the call shape. FFmpegEncoder is now static and stream-based:
        //   await FFmpegEncoder.EncodeAsync(source, writer, "-f f32le -ar 48000 -ac 2", token);
        // Wire `source` from PlayerQueue and `writer` to a Stream, and this becomes one line.
        await Task.CompletedTask;
    }
}