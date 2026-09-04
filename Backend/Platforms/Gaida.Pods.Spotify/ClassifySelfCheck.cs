namespace Gaida.Pods.Spotify;

/// <summary>
///     ponytail: the one runnable check for Classify's parsing -- no test project, just asserts that fail
///     loudly. Run with `dotnet run -- --self-check`.
/// </summary>
public static class ClassifySelfCheck
{
    public static void Run()
    {
        Expect(Classify.Parse("spotify://4cOdK2wGLETKBW3PvgPWqT"), 200, "id", "spotify://4cOdK2wGLETKBW3PvgPWqT",
            null);
        Expect(Classify.Parse("spotify:track:4cOdK2wGLETKBW3PvgPWqT"), 200, "id",
            "spotify://4cOdK2wGLETKBW3PvgPWqT", null);
        Expect(Classify.Parse("https://open.spotify.com/track/4cOdK2wGLETKBW3PvgPWqT?si=abc"), 200, "id",
            "spotify://4cOdK2wGLETKBW3PvgPWqT", null);
        Expect(Classify.Parse("https://open.spotify.com/intl-de/track/4cOdK2wGLETKBW3PvgPWqT"), 200, "id",
            "spotify://4cOdK2wGLETKBW3PvgPWqT", null);
        Expect(Classify.Parse("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M"), 200, "playlist",
            "spotify-playlist://37i9dQZF1DXcBWIGoYBM5M", null);
        Expect(Classify.Parse("spotify-playlist://37i9dQZF1DXcBWIGoYBM5M"), 200, "playlist",
            "spotify-playlist://37i9dQZF1DXcBWIGoYBM5M", null);
        Expect(Classify.Parse("spotify://short"), 400, null, null, "The Spotify track ID is invalid.");
        Expect(Classify.Parse("https://open.spotify.com/album/4cOdK2wGLETKBW3PvgPWqT"), 400, null, null,
            "The Spotify link is not a track or a playlist.");

        // Not ours: another platform's link, plain text, nothing at all.
        Expect(Classify.Parse("https://youtu.be/dQw4w9WgXcQ"), 404, null, null, null);
        Expect(Classify.Parse("hello world search text"), 404, null, null, null);
        Expect(Classify.Parse(""), 404, null, null, null);
        Expect(Classify.Parse(null), 404, null, null, null);

        Console.WriteLine("ClassifySelfCheck: OK");
    }

    private static void Expect(ClassifyResult actual, int status, string? kind, string? id, string? error)
    {
        if (actual.Status == status && actual.Kind == kind && actual.Id == id && actual.Error == error) return;

        throw new Exception(
            $"ClassifySelfCheck failed: expected ({status},{kind},{id},{error}) got " +
            $"({actual.Status},{actual.Kind},{actual.Id},{actual.Error})");
    }
}
