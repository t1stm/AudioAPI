# Local variants on the roll

Confirmed: **hero only**, **prompt on press**, **renditions offered in one
direction only**, and the weak-match band **set by calibration, not by this
document**. One question is still open, at the bottom.

When the roll lands on a YouTube track, the library often already has the same
recording, in FLAC, without an ad and without a 12-second channel intro. Today
nothing says so. This plan makes the API tell us, and makes the hero's Play and
Add buttons offer the swap at the moment of the press.

Source of truth for the API is `~/RiderProjects/AudioAPI/API.md`. Everything
below leans on code that is already in `Gaida.Core.Utils` — the normalizer is
the only genuinely new logic.

---

## 1. What already exists

`MusicManager.SearchByTerm` is 90% of this feature already:

- `ParentesisRegex()` strips `(…)` before comparing — the `(Official Video)`
  case is half-solved.
- `LevenshteinDistance.RemoveFormatting` drops punctuation and case, so
  `"Are, darpay"` and `"ARE, DARPAY"` are the same string.
- `ScoreSingleTerm` already compares against **both** `RomanizedTitle`/`Author`
  and `OriginalTitle`/`Author`, and already tries `title+artist` and
  `artist+title` concatenations in both orders.
- `Romanize.FromCyrillic` already exists and is what wrote `RomanizedTitle` in
  the first place.

Three things are missing, and they are the whole job:

1. **It returns a bool, not a score.** `< 2` and `< 3` are absolute Levenshtein
   thresholds. A 3-character title and a 30-character title get the same budget,
   and there is no "how close" to rank on or to threshold against.
2. **It takes one flat term.** A YouTube video title is not a search term. It is
   an artist, a title, a script, a set of tags, and some junk, jammed together
   by whoever uploaded it.
3. **The absolute threshold is too tight for transliteration.** `Митничарю`
   through `Romanize.FromCyrillic` is `Mitnicharyu`; the library file is
   `Mitnichariu`. That is a distance of 2 — the existing `< 2` **rejects it**.
   `ю→yu|iu`, `я→ya|ia`, `ъ→u|a`, `й→y|i`, `щ→sht|sh` are all ambiguous, and a
   long title accumulates several. Scoring has to be length-relative.

So: a new `TitleNormalizer` that turns one messy video title into a handful of
clean `(artist, title)` candidates, and a scored variant of `ScoreSingleTerm`
that ranks the library against all of them. Nothing else in the search path
changes; `SearchByTerm` keeps working exactly as it does.

---

## 2. The normalizer

`Gaida/Gaida.Core/Utils/TitleNormalizer.cs`, beside `LevenshteinDistance` and
`Romanize`. Pure functions, no state, no I/O.

Input is the YouTube result's `Name` (video title) and `Artist` (channel title).
Output is a list of candidates plus the rendition tags found.

### A. Strip a trailing year

`", 2019"`, `"(2019)"`, `"[2019]"` — anchored to end-of-string, exactly four
digits. Run once on the whole title and again on each segment after the split
in §C, since the year can ride on either side of a slash.

Anchoring matters: `"ARE, DARPAY"` has a comma in the title itself. A greedy
comma-strip would eat half the song.

### B. Classify and strip bracketed tags

Everything in `(…)` or `[…]` comes out of the search text, but **what it was**
is recorded, because that is the difference between "you have this" and "you
have a different take on this".

| class         | contents                                                                                                                    | effect                                       |
| ------------- | --------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------- |
| **noise**     | official video/audio/music video, lyrics, lyric video, visualizer, mv, hd, hq, 4k, feat./ft./featuring, prod. by, subtitles | dropped, no effect on the verdict            |
| **rendition** | instrumental, live, acoustic, remix, radio edit, extended, cover, karaoke, demo, unplugged, sped up, slowed, clean, 8d      | dropped from the text, **recorded as a tag** |
| **unknown**   | anything else                                                                                                               | dropped, lowers the score slightly           |

The identical classifier runs over the library side too — `RomanizedTitle` is
where a library `(Instrumental)` lives, and it has to come off before the titles
can be compared at all.

Comparing the two tag sets is the whole verdict, with no branch per tag: equal
sets mean the same recording, and a tagged upload against an untagged library
track means your plain copy of a live or instrumental version. See §2G for why
that only runs the one way.

`feat.` sits under noise deliberately. `"Sonne (feat. Somebody)"` and `"Sonne"`
are the same recording as far as your library is concerned — the featured
artist is metadata, not a rendition.

### C. Split dual-script titles, when the halves balance

`"Lia - Mitnichariu/ Лия - Митничарю"` is two spellings of one song, and either
side may be the one that matches. `"AC/DC - Back in Black"` is one spelling of
one song with a slash in the band's name.

**Separators.** `/` `|` `\` `~` and their fullwidth and Unicode twins
`／` `｜` `∕` `⁄` `〜`. Split on any of them, all in one pass.

Dashes are deliberately **not** in the set. `-`, `–` and `—` are how a title
separates its artist from its title, which is §D's job — putting them here would
shred every well-formed title in the library.

**Noise segments come out first.** The pipe earns its place in the set but is
used for metadata far more than for script twins: `"Rammstein - Sonne | Official
Video"` is the common shape, not the exception. So each segment is run through
§B's noise vocabulary, and a segment that is _entirely_ noise is dropped before
anything is weighed. Same list, same classifier, no second vocabulary to keep in
sync.

**Then the balance rule.** Take the longest surviving segment and keep every
segment within a 65/35 split of it:

```
keep segment if   len(segment) / len(longest segment) >= 0.54

>= 2 kept  ->  those segments are the candidates
   1 kept  ->  no split; the unsplit title is the only candidate
```

A script pair is close to even by construction — Cyrillic is compact, and the
expansions that romanize it (`щ→sht`, `ю→yu`, `я→ya`) stretch the Latin side
back out to roughly the same length. A separator inside a name is lopsided,
because one side is a fragment.

| title (post-strip)                                | segments     | kept | outcome  |
| ------------------------------------------------- | ------------ | ---- | -------- |
| `Lia - Mitnichariu/ Лия - Митничарю`              | 17 / 16      | 2    | split    |
| `EMANUELA - ARE, DARPAY / Емануела - Аре, дърпай` | 22 / 22      | 2    | split    |
| `Rammstein - Sonne \| Official Video`             | 17 / _noise_ | 1    | no split |
| `Lia - Mitnichariu / Лия - Митничарю \| Payner`   | 17 / 16 / 6  | 2    | split    |
| `AC/DC - Back in Black`                           | 2 / 18       | 1    | no split |

The two outcomes are exclusive — segments, or the unsplit title, never both. No
blocklist, no band names in the source, and no junk `AC` or `DC - Back in Black`
candidate left in the pool to trip the threshold against some unrelated song.

**Order matters.** This runs after §A and §B, never before. Measured raw,
`"Artist - Title (Official Video) / Артист - Заглавие"` fails on the weight of
the tag alone — the tag has to be gone before the halves are weighed.

`0.54` is a calibration knob like `strong` and `weak`, and gets the same
treatment in §5.

### D. Split artist from title

`" - "` is the separator, matching `MusicManager.ParseFile`'s own filename
convention. Emit the split both ways round (`artist - title` and `title -
artist`) as separate candidates rather than guessing which the uploader used.

The channel title is an additional artist candidate, after stripping `VEVO`,
a trailing `Official`, and a trailing `- Topic` (YouTube's auto-generated
artist channels are the cleanest artist string available when they appear).

### E. Romanize the Cyrillic candidates

Any candidate containing Cyrillic gets a romanized twin via
`Romanize.FromCyrillic`. That is what lets a Cyrillic-only title reach a library
entry that only has `RomanizedTitle` filled in.

### F. Score

For each candidate against each library song:

```
sim(a, b) = 1 - Levenshtein(a, b) / max(len(a), len(b))     // on RemoveFormatting'd strings
score     = 0.65 * max(sim over title fields)               // Romanized + Original
          + 0.35 * max(sim over artist fields)
```

Weighted so neither half can carry a match alone: a perfect artist with a wrong
title tops out at 0.35, a perfect title with a wrong artist at 0.65. Both are
rejected. That is the guard against a library full of one artist matching
everything they ever released.

The existing concatenation comparisons (`titleartist`, `artisttitle`) stay as a
fallback for candidates where the `" - "` split failed and the whole string is
the title.

`AsParallel()` over `Songs`, the pattern `SearchById` already uses.

```
// ponytail: linear scan × ~6 candidates over the whole library, same cost shape as
// SearchByTerm. Prefilter on a first-letter or length bucket if the library outgrows it.
```

### G. Verdict

| best score | rendition tags                       | verdict               |
| ---------- | ------------------------------------ | --------------------- | --------- | ------ |
| ≥ `strong` | sets equal                           | `same`                |
| ≥ `strong` | YouTube tagged, **library untagged** | `variant`             |
| ≥ `strong` | library tagged                       | **no match**          |
| ≥ `weak`   | as above                             | `weak`, and only if ` | Δduration | ≤ 20s` |
| below      | —                                    | no match              |

**Renditions go one way only.** A `(Instrumental)` upload may be answered with
your plain studio copy; a plain upload is never answered with your instrumental.
An untagged library track is the safer thing to hand someone, and the asymmetry
falls out of one condition — `libraryTags.Count == 0` — rather than a per-tag
policy.

That condition is also why `(Remastered)` stays classified as **noise** and not
as a rendition: a remaster is the same performance, and demoting it would make
every remastered file in your library permanently unofferable.

`Δduration` is always reported, never used to reject a `same`/`variant`: YouTube
uploads carry intros, outros and silence, so a 12-second delta is normal and not
evidence of a wrong match. It is the client's job to show it.

`strong` and `weak` are deliberately unnumbered here. They come out of the
calibration pass in §5, and `weak` may come out as "there is no weak band" —
the code path exists either way, and setting `weak == strong` switches it off
with no branch to delete.

---

## 3. The endpoint

`Gaida.API/Controllers/Query.cs`, beside `FindQueryType`, which is the same kind
of question.

```
GET /Audio/Local/Variant?name=…&artist=…&duration=00:04:32
```

`200` with the body below, `204` when nothing matches.

```json
{
  "match": "same",
  "score": 0.97,
  "durationDeltaSeconds": 11,
  "youTubeTags": [],
  "libraryTags": [],
  "result": { "id": "audio://ramsonne-x9", "name": "Sonne", "artist": "Rammstein", … }
}
```

Two things about that shape:

- **The client sends the title, not the id.** The endpoint then touches nothing
  but the in-memory `Songs` list — no YouTube call, no cache lookup, no upstream
  dependency, which is what "searches the local database only" has to mean if it
  is going to run after every roll.
- **`result` is a plain `SearchResultDto`.** It goes straight into `queue.add`
  with no new client-side type and no mapping. `DiscoveryResultMapper.Map` is
  reused unchanged.

`Gaida.API/Contracts/DiscoveryContracts.cs` gains one `LocalVariantDto` record.
One line in `API.md`.

---

## 4. The client

### 4.1 The request — `src/requests/songs.ts`

```ts
export type LocalVariant = {
	match: 'same' | 'variant' | 'weak';
	score: number;
	durationDeltaSeconds: number;
	youTubeTags: string[];
	libraryTags: string[];
	result: SearchResult;
};

export async function getLocalVariant(song: SearchResult, fetcher: Fetcher) {
	if (!song.id.startsWith('yt://')) return null;
	const query = `name=${encodeURIComponent(song.name)}&artist=${encodeURIComponent(song.artist)}&duration=${song.duration}`;
	const variant = await getJson<LocalVariant | null>(fetcher, `/Local/Variant?${query}`);
	if (variant) variant.result = proxyThumbnails([variant.result])[0];
	return variant;
}
```

The `204` needs no handling: `getJson` already does `.json().catch(() => null)`
and returns the null on an ok response. The `yt://` guard is the whole "don't
ask about a roll that already came from the library" rule, in one line, at the
one place every caller goes through.

### 4.2 The state — `src/routes/(app)/+page.svelte`

```ts
let variant = $state<LocalVariant | null>(null);
let pending = $state<'play' | 'queue' | null>(null);
let rollToken = 0;
```

`rollAgain()` clears both, increments `rollToken`, and only assigns the lookup's
result if the token still matches — a fast second roll must not have the first
roll's suggestion land on it.

`playHero` / `queueHero` become: if `variant` and not already `pending`, set
`pending` and return; otherwise act as they do now. Choosing either button in
the prompt clears `pending`.

In a room there is no Play at all, and `queue.add` sends `add <id>` — swapping
the id is free, and swapping to a library track means the room streams a local
file instead of going out to YouTube for everyone. The prompt is _more_ worth
having in a room, not less.

### 4.3 The prompt

Nothing appears until the press. The hero looks exactly as it does today:

```
┌ The roll ─────────────────────────────────────────────────┐
│ ▓▓▓▓▓▓▓  Sonne                                            │
│ ▓▓▓▓▓▓▓  Rammstein                                        │
│ ▓▓▓▓▓▓▓  4:32                                             │
│                                                           │
│  [ ▶ Play ] [ Add to queue ] [ ↻ Roll again ]             │
│  ● Library 60%                         40% YouTube ●      │
└───────────────────────────────────────────────────────────┘
```

Press Play, and the button row is replaced in place — no overlay, no dimmed
page, no focus trap:

```
│  ┌ In your library ──────────────────────────────────┐    │
│  │ Sonne — Rammstein · FLAC · 4:33                   │    │
│  │ [ Play the library copy ] [ Play the YouTube one ]│    │
│  └───────────────────────────────────────────────────┘    │
```

A different take reads differently, and never claims to be the same recording.
This only ever runs one way — the upload carries the tag, your library copy is
the plain one — so the button names what the _upload_ was, not what you get:

```
│  ┌ The studio version is in your library ────────────┐    │
│  │ Sonne — Rammstein · FLAC · 4:30                   │    │
│  │ [ Play the studio version ] [ Play the live one ] │    │
│  └───────────────────────────────────────────────────┘    │
```

If the weak band survives calibration, a weak match admits it and shows the one
fact that lets you judge:

```
│  ┌ Possibly the same track ──────────────────────────┐    │
│  │ Sonne — Rammstein · FLAC · 4:33 · 14s shorter     │    │
│  │ [ Play the library copy ] [ Play the YouTube one ]│    │
│  └───────────────────────────────────────────────────┘    │
```

**Design.** No new tokens. `src/app.css` already says gold is the library and
ember is YouTube — the whole roll slider is built on that, and the `Local`/
`YouTube` badge in `Song.svelte` uses the same pair. The prompt inherits it: a
gold hairline border and a gold eyebrow for `same`, and for `variant` the same
frame with the upload's rendition tag set in ember inside an otherwise
`text-fog` line, so the qualification reads as _what you are leaving behind_
rather than as a warning about what you are getting. `weak`
keeps the frame in `border-haze` and moves the eyebrow to `text-fog` — an
uncertain suggestion should look uncertain.

The eyebrow is the existing `@utility eyebrow` (mono, tracked, uppercase), which
is what every structural label in this app already is. The transition is a
150ms opacity and height, matching the row's existing hover timing, and is
dropped entirely under `prefers-reduced-motion` — the water/ripple idea belongs
to landing in the queue, and reusing it here would spend it twice.

**Copy.** The verb stays the same from press to press: Play stays "Play", Add
stays "Add" ("Add the library copy" / "Add the YouTube one"). The eyebrow states
a fact about your library, not about the system's confidence — "In your
library", not "Match found". The rendition case names the rendition in the
button, because "Play the instrumental" tells you what you get and "Play the
variant" does not.

**Accessibility.** Focus moves to the first button of the prompt on open.
Escape restores the original row and returns focus to the button that was
pressed. The frame is a `role="group"` with an `aria-label` naming the choice,
and an `aria-live="polite"` region announces the suggestion once. No modal, no
trap — this is a fork in a row of buttons, not a dialog.

**Files.** All of it inline in `+page.svelte` for now; it is one block of markup
with three text variations. It becomes `src/components/home/variant/` the moment
the picks grid needs it too (question 1).

---

## 5. Calibrating and checking

The thresholds in §2G are made up, and a library is not a spec — it is whatever
tagging habits you had over fifteen years of files. Two things, in order:

**`Gaida/Gaida.Tests/TitleNormalizerTests.cs`** — the normalizer is pure, so it
gets ordinary xunit cases, starting with the three real ones:

| input                                                   | expected best candidate                                        |
| ------------------------------------------------------- | -------------------------------------------------------------- |
| `Rammstein - Sonne (Official Video)`                    | `Sonne` / `Rammstein`, tags `[]`                               |
| `Lia - Mitnichariu/ Лия - Митничарю`                    | `Митничарю` / `Лия` **and** `Mitnichariu` / `Lia` both present |
| `EMANUELA - ARE, DARPAY / Емануела - Аре, дърпай, 2019` | `Аре, дърпай` / `Емануела`, year gone, comma intact            |
| `AC/DC - Back in Black`                                 | one segment kept, unsplit is the only candidate                |
| `Rammstein - Sonne \| Official Video`                   | noise segment dropped, no split                                |
| `Artist - Title (Official Video) / Артист - Заглавие`   | splits — tag stripped before weighing                          |
| `Sonne (Instrumental)`                                  | `Sonne`, tags `["instrumental"]`                               |

Trace of the second one, since it is the case that breaks the current code: the
gate passes at 0.94 and the two segments become candidates, plus a romanized
twin of the Cyrillic one. The Cyrillic segment matches `OriginalTitle`/
`OriginalAuthor` exactly at 1.0, while its romanized twin only reaches ~0.82
against `Mitnichariu` because of the `ю→yu|iu` ambiguity. Scoring every
candidate and taking the max is what makes the exact hit available at all —
scoring only the romanized form would land it in the weak band, or miss it.

**A calibration pass over the real library**, in the spirit of
`tools/roomSim.mjs` in `SYNC_PLAN.md`: score every title in the YouTube cache
against the library, dump `score / verdict / both titles` as CSV, and read it.
`strong` and `weak` get set from what that shows, not from this document.

False positives are the failure mode that makes a feature like this annoying
rather than useful, so the pass is looking for the score at which the wrong
answers start. `strong` goes just above it. `weak` covers whatever honest band
is left below — including none, if the Cyrillic near-misses and the wrong
answers turn out to sit at the same score, in which case `weak == strong`
switches the band off with no code to delete and the third prompt state never
renders.

---

## 6. Build order

1. `TitleNormalizer` + its tests. No API, no client. Nothing else can be
   calibrated until this exists.
2. `MusicManager.Match.cs` (a partial — `MusicManager` is already `partial`)
   with the scored match. Reuses `LevenshteinDistance`, changes nothing existing.
3. The calibration pass. Set `strong` and `weak`, or find there is no `weak`.
4. `GET /Audio/Local/Variant` + `LocalVariantDto` + the `API.md` line.
5. `getLocalVariant` in `songs.ts`.
6. The hero state, the race guard, the prompt.

Steps 1–3 are the risk. 4–6 are plumbing.

---

## Still open

**`(Remastered)` — noise or rendition?** It is a different master of the same
performance, and the answer is load-bearing now that renditions only run one
way. Classified as **noise** (as drafted): a remastered library copy is offered
as a straight swap. Classified as **rendition**: every remastered file in the
library becomes permanently unofferable, because the rule only offers untagged
library tracks. Noise is the drafted answer for exactly that reason — say if you
disagree.

Everything else is settled: hero only, prompt on press, renditions one way,
thresholds from calibration.
