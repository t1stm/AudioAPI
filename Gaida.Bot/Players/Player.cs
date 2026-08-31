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

        var ffmpeg = new FFmpegEncoder();
        ffmpeg.Convert("-f f32le -ar 48000 -ac 2");
    }
}