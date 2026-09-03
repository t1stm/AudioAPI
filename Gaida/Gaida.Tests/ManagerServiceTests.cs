using Gaida.API;
using Serilog.Core;

namespace Gaida.Tests;

public class ManagerServiceTests
{
    /// <summary>
    ///     Every request for the same codec/bitrate/id shares one encoder. A second ffmpeg writing into the same
    ///     stream spreader interleaves its output, so racing requests must all wait on the first one's start.
    /// </summary>
    [Fact]
    public async Task StartsOneEncodePerKeyUnderARace()
    {
        var managerService = new ManagerService(Logger.None);
        var gate = new TaskCompletionSource();
        var starts = 0;
        var accepted = 0;

        var requests = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            if (managerService.TryGetEncoder("key", out var cached)) return await cached;

            var encoder = managerService.GetOrStartEncoderAsync("key", async _ =>
            {
                Interlocked.Increment(ref starts);
                await gate.Task;
                return true;
            }, out var started);

            if (started) Interlocked.Increment(ref accepted);
            return await encoder;
        })).ToArray();

        await Task.Delay(100);
        gate.SetResult();
        var encoders = await Task.WhenAll(requests);

        Assert.Equal(1, starts);
        // Preload answers 202 off `started`, so exactly one racer may see it.
        Assert.Equal(1, accepted);
        Assert.All(encoders, encoder => Assert.Same(encoders[0], encoder));
    }

    /// <summary>A start that never got off the ground must not poison the key for every later request.</summary>
    [Fact]
    public async Task FailedStartIsNotCached()
    {
        var managerService = new ManagerService(Logger.None);

        Assert.Null(await managerService.GetOrStartEncoderAsync("key", _ => Task.FromResult(false), out _));
        Assert.False(managerService.TryGetEncoder("key", out _));
        Assert.NotNull(await managerService.GetOrStartEncoderAsync("key", _ => Task.FromResult(true), out _));
    }
}
