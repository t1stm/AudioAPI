# Music Database format: variant arrays

> **Status:** implemented, except Phase 3. The tag fix, the variant arrays, the legacy read shim, the
> tag re-read migration, artist splitting and the unified scorer are all in. Phase 3 (deleting the
> shim) waits until a production load has rewritten every `Info.json`.

Replace the four fixed name fields on `MusicInfo` with two ordered arrays, and make every search
compare every variant instead of four hand-picked pairs.

## What the real library says

Measured over `/nvme0/DiscordBot/Music Database` — 3671 songs, 666 `Info.json` files:

| | count | share |
|---|---|---|
| `RomanizedTitle == OriginalTitle` | 1982 | 54% |
| `RomanizedAuthor == OriginalAuthor` | 2311 | 63% |
| `Romanized*` field that is **not Latin script** | 1301 | 35% |
| artist strings containing `&`, `feat.`, `ft.`, `,`, `vs.` | 663 | 18% |

And the finding that reframes the whole question — **84% of the library has never had its tags
read at all**:

| extension | files | tags read today? |
|---|---|---|
| `.wv` | 3066 | **no** |
| `.mp3` | 571 | yes |
| `.ogg` | 26 | **no** |
| `.flac` | 8 | **no** |

`MediaInfo.GetInformation` looks up `tags.TryGetProperty("artist")` and `("title")`
(`MediaInfo.cs:45,51`). `JsonElement.TryGetProperty` is **case-sensitive**, and ffprobe only
lowercases ID3v2 keys — Vorbis comments and APEv2 (`.wv`, `.flac`, `.ogg`) come through verbatim,
which by convention means `ARTIST`, `TITLE`, `ALBUM`. In a 300-file sample: 239 uppercase `ARTIST`,
48 lowercase, and the lowercase ones were the mp3s.

Proof, not inference — a `.wv` whose tag and filename disagree:

```
file           Queen - You_re My Best Friend.wv
  TAG          title="You're My Best Friend"   artist="Queen"
  FILENAME     title='You_re My Best Friend'
  Info.json →  "OriginalTitle": "You_re My Best Friend"      ← the filename won
```

The underscore is the filesystem-safe substitution in the *filename*. It is in the database. The
apostrophe from the tag never got there. So for 3100 of 3671 songs, every name in the database is
**parsed from the path**, not read from the file — which is why `RomanizedAuthor` is a folder name
3133 times, why 35% of the "romanized" fields are not Latin, and where `Оркестър Имперал`
(folder, typo) beats `Оркестър Империал` (tag, correct).

Three things follow, and they shape the design more than the schema does:

1. **The `Romanized*` fields already lie.** `RomanizedAuthor` is the *folder name*
   (`MusicManager.ParseFile` line 129: `entry.RomanizedAuthor ??= romanizedAuthor.Trim()`), and
   `MediaInfo` only ever romanizes Cyrillic. So `"RomanizedTitle": "あなぐらぐらし"` and
   `"RomanizedAuthor": "Братя Аргирови"` are both in there, 1301 times. Any flag written at import
   time saying "this entry has a romanization" would be **wrong on a third of the library**.
2. **18% of artists are compound and nothing splits them.** `SearchByTerm("Sayuki")` misses
   `Maki & Sayuki` today: `RemoveFormatting` collapses it to `makisayuki`, and
   `ComputeStrict("makisayuki", "sayuki") == 5`, well past the `< 2` budget. This is the biggest
   *user-visible* win in the whole plan, bigger than the schema change.

## Determining the Artists

The honest answer is that **no single source is authoritative, and the array schema means you no
longer have to pretend one is.** Today the code picks one winner per field and throws the rest away;
that is what makes the choice hard. Keep them all and the problem mostly dissolves — `Maki & Sayuki`
(folder) and `Mako & Sayuki` (tag) both go in `Artists`, both are searchable, and the typo stops
mattering because you never had to decide which one was the typo.

What still needs deciding is **index 0** — the display name — and there the order of authority is:

| # | source | notes |
|---|---|---|
| 1 | `ARTISTS` tag | multi-value by convention; present on ~31% of files here |
| 2 | `ARTIST` tag | **the fix that unlocks 84% of the library** |
| 3 | filename, before `" - "` | current de-facto primary; loses `'` → `_` and other path-safe substitutions |
| 4 | folder name | last resort; wrong 538 times where it disagrees with the tag |

`ALBUMARTIST` is present on 73% of files but is `"Various Artists"` on every compilation rip, so it
is a fallback *below* the folder name and must be discarded on that literal value. Not worth wiring
up until something needs it.

All four are collected, trimmed, deduped, and stored in that order. Index 0 becomes the best
available name instead of "whatever the path said".

Read the tags case-insensitively — one helper, since `TITLE`/`ALBUM`/`LYRICS` have the same problem:

```csharp
// ffprobe passes Vorbis/APEv2 keys through verbatim (ARTIST), and lowercases only ID3v2 (artist).
private static string? Tag(JsonElement tags, params string[] names)
{
    foreach (var name in names)
        foreach (var tag in tags.EnumerateObject())
            if (tag.NameEquals(name) || string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase))
                return tag.Value.GetString();
    return null;
}
```

**The fix alone does nothing to the existing 3671 songs.** `NewFiles` (`MusicManager.cs:110`) only
parses files absent from `Info.json`, so every already-indexed entry keeps its path-derived names
forever. The re-read has to be part of the migration: when `WasLegacy` is set (Phase 1), re-run
`MediaInfo` for that entry and merge the tag values into the arrays ahead of the path-derived ones.
That is 3671 `ffprobe` invocations on one startup, across a `Parallel.ForEachAsync` that already
exists — a slow boot, once, and then never again.

### Splitting compound artists

Separator counts over all 3671 entries, both artist fields:

| separator | count | example |
|---|---|---|
| ` & ` | 603 | `Mako & Sayuki` |
| `feat.` / `ft.` | 189 | `Alisia feat. Konstantin` |
| `, ` | 56 | `Годжи, Гацо Бацов & Сашо Роман` |
| ` и ` (Bulgarian "and") | 33 | `Деси и Тони Стораро` |
| ` + ` | 1 | `Mike + The Mechanics` |
| ` / `, ` x `, ` vs `, ` with ` | **0** | — |

Two rules the data hands you for free:

- **` и ` must be in the set.** It is the Bulgarian "and" and it is 33 entries of this specific
  library. No generic music-tagging splitter would include it.
- **`&` must require surrounding whitespace.** `Rad&Co` is a band name, twice. `Слави Трифонов &
  Ку-ку бенд` is two artists.

And four separators that generic splitters include and this library does not need: `/`, `x`, `vs`,
`with`. Zero occurrences, all four are real substrings of real names. Leave them out.

## Schema

```json
{
  "ID": "брdo-vch-YO",
  "Titles":  ["До вчера", "Do vchera"],
  "Artists": ["Братя Аргирови", "Bratya Argirovi"],
  "Album": null,
  "CoverUrl": "$[DOMAIN]/054cb5….jpg",
  "RelativeLocation": "Bulgarian/Естрада/Братя Аргирови/Братя Аргирови - До вчера.wv",
  "Length": 221986
}
```

**Convention:** index 0 is the original, exactly as the tag or filename gave it. Later entries are
alternate readings, romanization first when there is one. Identical strings are never stored twice,
so the 54%/63% duplicate rows above collapse to a single entry and the file shrinks.

Compound artists stay **joined** on disk (`"Дони & Момчил"`, not `["Дони", "Момчил"]`). Splitting is
a match-time concern: the query side needs a splitter anyway for YouTube titles like
`Doni feat. Momchil - X`, so it is one function with two callers rather than a migration decision
taken per entry, and the tag's own join order survives for display.

### On `ContainsRomanized` — derive it, don't store it

You asked to be argued with here, so: the flag is right, the *storage* is not.

```csharp
[JsonIgnore] public bool ContainsRomanized =>
    Titles.Count > 1 && !IsLatin(Titles[0]) && IsLatin(Titles[1]);
```

Three reasons it should be computed:

- **No search path reads it.** Matching compares every entry in both arrays regardless of what
  index 1 *means*. The only consumer is display — "which of these do I show a Latin-script user" —
  and a one-line script check answers that from the data itself.
- **One bool cannot cover both arrays.** Titles and artists romanize independently in this library
  (`"RomanizedTitle": "あなぐらぐらし"` with `"RomanizedAuthor": "Kikuo"`), so a single flag is
  already ambiguous and two flags is the field count you were trying to reduce.
- **A stored bool drifts.** Per the table above it would be born wrong 1301 times, and every later
  hand-edit of an `Info.json` is another chance to desync it. A derived one cannot be wrong.

`Titles.Count > 1` on its own is *not* the same predicate, and that distinction is worth keeping:
once a second title is an English release name or an alias rather than a transliteration, "there is
an alternate" and "there is a romanization" diverge. When that day comes the answer is a per-entry
label (`"Titles": [{"text": …, "kind": "alias"}]`), not a bool — and the derived property above is
what you replace, with no on-disk migration needed because nothing was ever written.

**If you want it on the wire**, expose it from `PlatformResult` where the frontend can read it. That
costs nothing and keeps it out of the file.

## Phases

### Phase 0 — extract the variant expansion (no format change)

`MusicManager.Match.Score` (line 118) and `MusicManager.ScoreSingleTerm` (line 173) both hand-roll
"clean every name field, compare every pair". Pull that into one place *first*, against the current
four fields, so the schema swap in Phase 1 has exactly one function to touch.

```csharp
// MusicInfo.cs
/// <summary>Every string this song can be found by, cleaned once. Rebuilt only when the song is reloaded.</summary>
public sealed record SearchVariants(string[] Titles, string[] Artists, IReadOnlySet<string> Tags);

[JsonIgnore] private SearchVariants? _variants;
[JsonIgnore] public SearchVariants Variants => _variants ??= BuildVariants();
```

`BuildVariants` does what `Score` does today: `NormalizeLibrary` each title (collecting rendition
tags), `RemoveFormatting` each name, `Distinct()`. `Score` and `ScoreSingleTerm` both consume it.

This is also a free performance fix. `FindLocalVariant` runs "after every roll" and currently calls
`NormalizeLibrary` twice plus four `RemoveFormatting` calls **per song per call** — 3671 songs, every
time. Caching it on the entry makes that once per load.

*Check:* `LocalVariantTests` and `CalibrationTests` pass unchanged, same `StrongMatch`/`WeakMatch`
thresholds. Phase 0 is a pure refactor; if the calibration CSV moves at all, it is wrong.

### Phase 1 — the schema swap, with a read shim

`MusicInfo` gains `Titles`/`Artists` and loses the four fields as *serialized* members. Old files
keep loading via `IJsonOnDeserialized` — stdlib, no custom converter:

```csharp
public class MusicInfo : IJsonOnDeserialized
{
    public List<string> Titles { get; set; } = [];
    public List<string> Artists { get; set; } = [];

    // ponytail: read-only legacy shim. Setter-only properties are not serialized by
    // System.Text.Json, so nothing writes these names back. Delete in Phase 3.
    [JsonPropertyName("OriginalTitle")]  public string? LegacyOriginalTitle  { set => _legacy[0] = value; }
    [JsonPropertyName("RomanizedTitle")] public string? LegacyRomanizedTitle { set => _legacy[1] = value; }
    [JsonPropertyName("OriginalAuthor")] public string? LegacyOriginalAuthor { set => _legacy[2] = value; }
    [JsonPropertyName("RomanizedAuthor")]public string? LegacyRomanizedAuthor{ set => _legacy[3] = value; }

    [JsonIgnore] public bool WasLegacy { get; private set; }

    public void OnDeserialized()
    {
        // Original first regardless of the order the properties appear in the file.
        if (Titles.Count  == 0) { Titles  = Variants(_legacy[0], _legacy[1]); WasLegacy = true; }
        if (Artists.Count == 0) { Artists = Variants(_legacy[2], _legacy[3]); WasLegacy = true; }
    }

    private static List<string> Variants(params string?[] values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v))
              .Select(v => v!.Trim()).Distinct().ToList();
}
```

The `Distinct()` there is the dedupe: 1982 titles and 2311 artists lose their duplicate row on first
load, without a separate migration pass.

Migration writes itself back through the loader that already exists. `ParseArtistFolder` rewrites
`Info.json` whenever it finds new or stale entries — add one clause (`MusicManager.cs:100`):

```csharp
var migrated = existing.Any(m => m.WasLegacy);
if (stale == 0 && newFiles.Count == 0 && !migrated) return existing;
```

One full startup rewrites all 666 files. Idempotent: a converted file sets `WasLegacy` false and is
never rewritten again.

Callers to update — the whole surface, it is small:

| file | change |
|---|---|
| `MusicInfo.ToMusicResult` | `Name = DisplayTitle` (Latin variant if any, else `Titles[0]`), `OriginalTitle = Titles[0]`, same for artist |
| `MusicInfo.UpdateRandomId` | prefix from the Latin variant when present — **but see the ID note below** |
| `MediaInfo.GetInformation` | `Titles = Variants(tag, Romanize.FromCyrillic(tag))` |
| `MusicManager.ParseFile` | fills `Titles`/`Artists` from tag → filename → folder, in that order |
| `MusicManager.SearchById` log, `CalibrationTests:79` | `song.Titles[0]` etc. |
| `MusicManagerTests`, `LocalVariantTests` | construct with arrays |

`PlatformResult` (`Name`/`Artist`/`OriginalTitle`/`OriginalArtist`) **does not change**. That is the
wire contract the frontend and the Discord bot read, and the mapping above keeps it byte-identical
for existing data. The entire diff stays inside `Gaida.Platforms.MusicDatabase`.

*Check:* one test that round-trips a legacy JSON blob → `MusicInfo` → serialize, asserting the output
has `Titles`/`Artists`, no `Romanized*`, and `Titles[0]` is the *original* even though
`RomanizedTitle` came first in the input. That last assertion is the one that catches a property-order
regression, which is the only way this shim can silently go wrong.

### Phase 2 — compare every variation, and split compound artists

One new public function in `TitleNormalizer` (it already owns the vocabulary — `feat`/`ft`/`featuring`
are in its `Noise` list, they just never split an *inline* artist string):

```csharp
/// <summary>"Doni & Momchil" → ["Doni & Momchil", "Doni", "Momchil"]. The joined form stays first:
/// an exact tag match must outrank a one-name match.</summary>
public static IReadOnlyList<string> SplitArtists(string? artist)
```

```csharp
// ponytail: separator set is measured off this library, not guessed — see the table above. "и" is
// the Bulgarian "and" (33 entries). "/", "x", "vs" and "with" score zero here and are all real
// substrings of real names, so they stay out. The whitespace around & is load-bearing: "Rad&Co".
[GeneratedRegex(@"\s+(?:&|\+|и|feat\.?|ft\.?|featuring)\s+|\s*,\s*", RegexOptions.IgnoreCase)]
private static partial Regex ArtistSeparatorRegex();
```

Parts under 2 characters are dropped. `BuildVariants` (Phase 0) runs artists through it, so both
search paths get compound matching from one edit.

Then `ScoreSingleTerm` collapses from ~30 lines of four hand-picked pairs to every pair:

```csharp
private static bool ScoreSingleTerm(string term, MusicInfo song)
{
    var (titles, artists, _) = song.Variants;

    return artists.Any(a => LevenshteinDistance.ComputeStrict(a, term) < 2)
        || titles.Any(t => LevenshteinDistance.ComputeStrict(t, term) < 2)
        || titles.Any(t => artists.Any(a =>
               LevenshteinDistance.ComputeStrict(t + a, term) < 3 ||
               LevenshteinDistance.ComputeStrict(a + t, term) < 3));
}
```

This is strictly more permissive than today, which is the point: the current code compares
`romanizedTitle+originalArtist` but never `originalTitle+romanizedArtist` in that order, and never
either one against a split artist. `Score` in `Match.cs` gets the same treatment — it already loops
`titles × artists`, it just reads `song.Variants` instead of rebuilding them.

`IsArtistPartOfSong` (`MusicManager.cs:234`) becomes a `.Any` over `Variants.Artists`, which is what
finally makes `/Artist/Sayuki` return the `Maki & Sayuki` tracks.

*Check:* three asserts in `MusicManagerTests` — `SearchByTerm("Sayuki")` finds `Maki & Sayuki`,
`SearchByTerm("Momchil")` finds `Дони & Момчил`, and `SearchByTerm("Doni & Momchil")` still finds it
by the joined form. Plus `CalibrationTests` re-run over the real library: **thresholds are calibration
knobs, not constants** (`Match.cs:33`) — a more permissive scorer may need `StrongMatch`/`WeakMatch`
nudged up, and the CSV is how you find out. Do not skip this; it is the whole reason that harness exists.

### Phase 3 — delete the shim

After one production startup has rewritten all 666 files (`grep -rl RomanizedTitle` over the storage
directory returns nothing), delete the four legacy properties, `OnDeserialized`, `WasLegacy`, and the
`migrated` clause. ~20 lines out.

## Deliberately not in this plan

**ID generation** — with playlist stability set aside, fold this into Phase 1. `UpdateRandomId`
builds its prefix from `RomanizedAuthor`, which is frequently not Latin, so live IDs like
`audio://брdo-vch-YO` and `audio://kiあなぐらぐらし-NP` exist and travel in URLs. Once `Artists` is an
array, pick the first ASCII-only variant for the prefix and fall back to random characters when
there is none. Note that this re-rolls IDs for the non-Latin entries, so anything holding a stored
`audio://` reference to one of them loses it.

**Lyrics search is not designed for here.** Arrays of names do not get you closer to it: matching a
query against a 2000-character lyric sheet with Levenshtein is O(n·m) per song across 3671 songs, so
that feature wants an inverted index or trigram set, which is a different data structure and a
different file (a sidecar, or a `Lyrics` field loaded lazily — lyrics in `Info.json` would inflate every
load for a feature most queries never touch). One useful fact for when you get there: **20% of the
sampled files already carry a `LYRICS` tag** (the Deezer-sourced rips do), so the corpus is largely
already on disk and the case-insensitive `Tag()` helper above is what reads it. This plan does not
block that work and does not pretend to prepare for it.

## Order and cost

The case-sensitive tag lookup is a **five-line fix worth more than the rest of the plan combined** —
it is the difference between naming 16% of the library from its metadata and naming 100% of it. Ship
it first, on its own, against the current fields; it changes nothing until the migration re-reads,
which is why the re-read belongs to Phase 1.

Tag fix (~15 min, one assert on the `Queen - You_re…` file) → Phase 0 (~1h, pure refactor, also a
real perf win) → Phase 1 (~2h + one slow boot for the re-read) → Phase 2 (~2h + a calibration run) →
Phase 3 (10 min, after a deploy). Phases 0 and 2 are independently valuable; if the schema change
stalls, Phase 2's artist splitting still ships on the old fields.
