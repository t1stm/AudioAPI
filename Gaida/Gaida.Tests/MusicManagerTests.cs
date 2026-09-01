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
            OriginalTitle = "Притури се планината",
            RomanizedTitle = "Prituri se planinata",
            OriginalAuthor = "Стефка Съботинова",
            RomanizedAuthor = "Stefka Sabotinova"
        };
        var manager = new TestMusicManager(song);

        var results = manager.SearchByTerm("Stefka Sabotinova").ToList();

        Assert.Equal([song], results);
    }

    private static MusicInfo Song(string id, string title, string artist)
    {
        return new MusicInfo
        {
            ID = id,
            OriginalTitle = title,
            RomanizedTitle = title,
            OriginalAuthor = artist,
            RomanizedAuthor = artist
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
