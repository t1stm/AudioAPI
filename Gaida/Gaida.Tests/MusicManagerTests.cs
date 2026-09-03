using Gaida.Platforms.MusicDatabase;
using Gaida.Platforms.MusicDatabase.Manager;

namespace Gaida.Tests;

public class MusicManagerTests
{
    [Fact]
    public void SearchByTermReturnsEverySongByMatchingArtist()
    {
        var firstSong = Song("first", "Around the World", "Daft Punk");
        var secondSong = Song("second", "Something About Us", "Daft Punk");
        var unrelatedSong = Song("unrelated", "One More Time", "The Marvelettes");
        var manager = new TestMusicManager(firstSong, secondSong, unrelatedSong);

        var results = manager.SearchByTerm("Daft Punk").ToList();

        Assert.Equal([firstSong, secondSong], results);
    }

    [Fact]
    public void SearchByTermMatchesRomanizedArtistName()
    {
        var song = new MusicInfo
        {
            ID = "romanized",
            Titles = ["Притури се планината", "Prituri se planinata"],
            Artists = ["Стефка Съботинова", "Stefka Sabotinova"]
        };
        var manager = new TestMusicManager(song);

        var results = manager.SearchByTerm("Stefka Sabotinova").ToList();

        Assert.Equal([song], results);
    }

    [Theory]
    [InlineData("Sayuki")]
    [InlineData("Maki")]
    [InlineData("Maki & Sayuki")]
    public void SearchByTermFindsEitherHalfOfACompoundArtist(string term)
    {
        var song = Song("wings", "Wings of Fire", "Maki & Sayuki");
        var manager = new TestMusicManager(song, Song("other", "One More Time", "Daft Punk"));

        Assert.Equal([song], manager.SearchByTerm(term).ToList());
    }

    [Fact]
    public void SearchByTermReachesEveryVariantOfTheName()
    {
        // The tag said "Mako", the folder said "Maki". Both are in the array, both are searchable, and
        // nothing had to decide which one was the typo.
        var song = new MusicInfo { ID = "wings", Titles = ["Wings of Fire"], Artists = ["Mako & Sayuki", "Maki & Sayuki"] };
        var manager = new TestMusicManager(song);

        Assert.Equal([song], manager.SearchByTerm("Maki").ToList());
        Assert.Equal([song], manager.SearchByTerm("Mako").ToList());
    }

    private static MusicInfo Song(string id, string title, string artist)
    {
        return new MusicInfo
        {
            ID = id,
            Titles = [title],
            Artists = [artist]
        };
    }

    private sealed class TestMusicManager : MusicManager
    {
        public TestMusicManager(params MusicInfo[] songs) : base(Serilog.Core.Logger.None)
        {
            Songs = [..songs];
        }
    }
}
