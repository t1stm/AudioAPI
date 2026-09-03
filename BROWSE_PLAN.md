# Library tree explorer

> **Status:** implemented, both halves. Phase 3 (deep links, "queue this folder", virtualization)
> is still open. Covers both repos — `AudioFrontend` (`./`) and `AudioAPI`
> (`~/RiderProjects/AudioAPI`). One document because it is one feature and the two
> halves are ~90 lines each.

An explorer over the Music Database's folder tree. Each click on a folder asks the API
for that folder's immediate children only; when a level contains audio files they render
as search rows, and playing one puts its `audio://` id in the queue.

## Why this is small

Three things are already in place, and they decide most of the design:

| already there | what it means for this feature |
| --- | --- |
| `MusicInfo.RelativeLocation` — `Bulgarian/Естрада/Братя Аргирови/… .wv` | the tree **is** the library. There is no tree to build, only to group by. |
| `MusicManager.Songs` — all 3671 entries in memory from boot | a level is a `foreach` over a list. No disk walk, no index, no cache, no invalidation. |
| `SearchRow.svelte` → `queue.playNow(result)` / `queue.add(result)` | "playing a file adds its `audio://` id to the queue" needs **zero new code**, in a room or out of one. `queue.add` already sends `add <id>` when `remote` is set. |

So the whole feature is: one endpoint that groups a list by path prefix, and one recursive
row component that calls it.

---

# Backend — `~/RiderProjects/AudioAPI`

## The endpoint

`GET /Audio/Browse?path={relative folder}` — omit or empty `path` for the root.

```json
{
  "path": "Bulgarian/Естрада",
  "folders": [
    { "name": "Братя Аргирови", "path": "Bulgarian/Естрада/Братя Аргирови", "songs": 42 }
  ],
  "files": [ /* ordinary discovery results, exactly as Search returns them */ ]
}
```

`folders` are the immediate subfolders, sorted case-insensitively, each with the number of
songs **anywhere beneath it** — the count is what makes a folder worth opening, and it is
free during the same scan. `files` are the songs directly in `path`, mapped by the existing
`DiscoveryResultMapper.Map`, so a browse result is byte-identical to a search result and the
frontend reuses its row.

**A path nobody has is an empty folder, not a 404.** Nothing here touches the disk, so
"unknown" and "empty" are the same answer and a second branch buys nothing. The UI renders
both as "This folder is empty."

## `MusicManager.Browse` — the only real logic

New method in `Gaida/Platforms/Gaida.Platforms.MusicDatabase/Manager/MusicManager.cs`, next
to `GetArtistSongs`:

```csharp
/// <returns>The folders and the songs directly inside <paramref name="path" />; empty when nothing is there.</returns>
public (List<(string Name, int Songs)> Folders, List<MusicInfo> Files) Browse(string path)
{
    // Nothing below touches the filesystem: the path is only ever compared against the
    // RelativeLocation strings already in memory, so "../" matches no prefix and escapes
    // nothing. Separators are '/' because ParseFile already splits on '/' (line ~140) and
    // production is Linux — a backslash is normalized rather than supported.
    var prefix = path.Replace('\\', '/').Trim('/');
    if (prefix.Length > 0) prefix += "/";

    var folders = new Dictionary<string, int>(StringComparer.Ordinal);
    var files = new List<MusicInfo>();

    foreach (var song in Songs)
    {
        if (song.RelativeLocation is not { } location ||
            !location.StartsWith(prefix, StringComparison.Ordinal)) continue;

        var rest = location[prefix.Length..];
        var slash = rest.IndexOf('/');

        if (slash < 0) files.Add(song);
        else folders[rest[..slash]] = folders.GetValueOrDefault(rest[..slash]) + 1;
    }

    return (
        folders.OrderBy(folder => folder.Key, StringComparer.OrdinalIgnoreCase)
            .Select(folder => (Name: folder.Key, Songs: folder.Value)).ToList(),
        files.OrderBy(song => song.DisplayTitle, StringComparer.OrdinalIgnoreCase).ToList());
}
```

`// ponytail: one linear scan of the whole song list per request. 3671 entries is
sub-millisecond and a prefix index would need invalidating on every rescan — build one only
if the library passes six figures.`

## Passthroughs

The same chain `GetArtistSongs` already takes, because `MusicSearchProvider.MusicManager` is
`protected` and the controller cannot reach past it:

- `MusicSearchProvider.Browse(path)` — returns the folders untouched and the files through
  `song.ToMusicResult(ContentDownloaders)`, the same mapping `ToResults` does.
- `MusicDatabase.Browse(path)` — one-line delegate to `_provider`.

## Contracts

Two records appended to `Gaida.API/Contracts/DiscoveryContracts.cs`:

```csharp
/// <param name="Songs">Songs anywhere beneath this folder, not just directly in it.</param>
public sealed record BrowseFolderDto(string Name, string Path, int Songs);

public sealed record BrowseDto(string Path, IReadOnlyList<BrowseFolderDto> Folders,
    IReadOnlyList<SearchResultDto> Files);
```

## Controller

New `Gaida.API/Controllers/Browse.cs`, shaped like `Artist.cs` — primary-constructor
`IConfiguration`/`IHostEnvironment`, `[FromServices] ManagerService`, `DiscoveryResultMapper.Map`
per file, drop the nulls. `path` is normalized once (`Replace('\\','/').Trim('/')`) so the
`path` it echoes back and the `folder.Path` it builds agree with what the client sent.

No `[ProducesResponseType]` for errors: there are none.

## The check

One `[Fact]` in `Gaida/Gaida.Tests/MusicManagerTests.cs`, using the `TestMusicManager` helper
already at the bottom of that file — it sets `Songs` directly, which is all `Browse` reads:

```csharp
[Fact]
public void BrowseReturnsOnlyTheImmediateChildrenOfAFolder()
{
    var manager = new TestMusicManager(
        At("Bulgarian/Естрада/Аргирови/a.wv"),
        At("Bulgarian/Естрада/Аргирови/b.wv"),
        At("Bulgarian/Народна/c.wv"),
        At("Bulgarian/loose.wv"));

    var (folders, files) = manager.Browse("Bulgarian");

    Assert.Equal([("Естрада", 2), ("Народна", 1)], folders);
    Assert.Single(files);                       // loose.wv, not the three below it
    Assert.Empty(manager.Browse("Bulgarian/Nope").Files);
}
```

(`At(location)` = a `MusicInfo` with that `RelativeLocation`, a title and an ID; the file's
existing `Song(...)` helper plus one property.)

That single fact covers the three things that can actually break: the prefix boundary
(`Bulgarian` must not match `Bulgarian2`), the file/folder split, and the recursive count.

## Docs

Append a **Library tree** section to `API.md` after *Artwork*, in the house voice: the shape
above, the "unknown path is an empty folder" rule, and the note that `files` are ordinary
discovery results.

---

# Frontend — `./`

## Interaction: expand in place, don't navigate

Folders open **inside** the page, each level indented under its parent with a hairline down
the left edge. That is what makes indentation mean anything — a page that replaces its
contents on every click has a breadcrumb but no visible tree.

Consequences, both deliberate:

- **No breadcrumbs.** The indentation is the trail; a second copy of it in the header is
  redundant.
- **No `?path=` in the URL, so a deep folder is not bookmarkable.** Skipped — add it when
  someone wants to send a folder link, at which point the load function seeds the open set
  from the query string.

## Files

| file | what |
| --- | --- |
| `src/requests/songs.ts` | `getBrowse` appended — reuses the module's `getJson`, `Fetcher` and `proxyThumbnails`. No new request module. |
| `src/routes/(app)/browse/+page.ts` | `getBrowse('', fetch)` — the root level only. |
| `src/routes/(app)/browse/+page.svelte` | page shell; renders the root's folders and files. |
| `src/components/browse/FolderRow.svelte` | one folder row, and its children when open. Recursive. |

### `getBrowse`

```ts
export type BrowseFolder = { name: string; path: string; songs: number };
export type BrowseLevel = { path: string; folders: BrowseFolder[]; files: SearchResult[] };

export async function getBrowse(path: string, fetcher: Fetcher) {
	const level = await getJson<BrowseLevel>(fetcher, `/Browse?path=${encodeURIComponent(path)}`);
	level.files = proxyThumbnails(level.files);
	return level;
}
```

`proxyThumbnails` is not optional — without it every cover in the explorer is blank inside the
Discord activity, the same trap every other request function in that file already avoids.

### `FolderRow.svelte`

```svelte
<script lang="ts">
	import Self from './FolderRow.svelte';
	import SearchRow from '$components/search/SearchRow.svelte';
	import { getBrowse, type BrowseFolder, type BrowseLevel } from '$requests/songs';
	import { ChevronRight, Folder, Icon } from 'svelte-hero-icons';

	const { folder }: { folder: BrowseFolder } = $props();
	let open = $state(false);
	let level = $state<BrowseLevel | null>(null);
	let failed = $state(false);

	// One call per folder for the life of the page: a closed folder keeps what it
	// fetched, so re-opening is instant and the library cannot change under us.
	async function toggle() {
		open = !open;
		if (!open || level) return;
		failed = false;
		level = await getBrowse(folder.path, fetch).catch(() => ((failed = true), null));
	}
</script>
```

The row is a real `<button aria-expanded={open}>`, not a `div` with `role="button"` —
`SearchRow` needs the div because it wraps links, this one wraps nothing, so the keyboard
and the screen reader come free.

Markup mirrors `SearchRow`'s grid so the two read as one list:
`grid grid-cols-[2.75rem_minmax(0,1fr)_auto] items-center gap-3 rounded-row px-2 py-2 hover:bg-surface-100 active:bg-surface-200` —
a `Folder` icon in the artwork slot (`size-11 rounded-art`, `text-gold`: gold is the library
everywhere else in this app), the name in `text-sm text-chalk`, the song count in
`font-mono text-[0.79rem] text-fog`, and a `ChevronRight` that rotates 90° when open
(`transition-transform`, `rotate-90`).

Children, when open:

```svelte
<div class="ml-2 border-l border-haze pl-2 sm:ml-5">
	{#each level.folders as child (child.path)}<Self folder={child} />{/each}
	{#each level.files as file (file.id)}<SearchRow result={file} />{/each}
</div>
```

That one wrapper is the whole "clear separators and indentations" requirement: a hairline in
`--color-haze` per level, folders before files. Depth needs no prop — the nesting supplies it.
The indent is 16px per level on a phone and 28px from `sm` up; four levels deep is 64px of a
320px screen, which a `SearchRow` still fits because its own layout is already single-column
below `sm`.

States, in fog, in place of the children: `Loading…` while the promise is out,
`This folder is empty.` for a level with neither folders nor files, and
`Could not load this folder.` with a retry on `failed` — a folder that fails to open must not
look like a folder that is empty.

### `+page.svelte`

`class="page mx-auto w-full max-w-5xl gap-6 p-4 sm:gap-9 sm:p-6 sm:pb-28"` — the search page's
own shell, unchanged. An `eyebrow text-gold` header reading `The library · {n} folders` with
the `h-px flex-1 bg-gold/35` rule the search page puts after its group heads, then the root's
folders and files in the same `flex flex-col`.

### Getting there

Two ways in, because the explorer is a place people return to:

- **A folder button in the header**, immediately right of the `musicrain` mark. The mark and the
  button are wrapped in one flex child so the row's `justify-between` distributes space *around*
  the pair rather than pushing the button out towards the search field, and the pair carries
  `sm:mr-2` so its own `gap-2` reads as tighter than its separation from the field. Haze and fog at
  rest, gold on hover, gold while `/browse` is the route. `hidden … sm:flex` — at 320px the header
  is a mark, a field and a face, and there is no room for a fourth thing.
- **A link in the home page's Artists in the library head**, next to the artist chips: those are
  one 200-track sample, this is the whole library.

---

## What changed while building it

- **The children container indents by `ml-[1.875rem] pl-1 sm:ml-8 sm:pl-2`, not the plan's
  `pl-2`/`sm:pl-3`.** `SearchRow` and `FolderRow` both carry their own `px-2`, so the container's
  padding was buying a second gutter — 12px per level back on a phone three levels down.
- **The header's folder button moved to sit beside the `musicrain` mark**, from its first position
  between the search field and the avatar, where `justify-between` left it floating near the centre.
- **`aria-controls` joins `aria-expanded`**, with the id from `$props.id()`. A folder name can hold
  spaces and Cyrillic, so the path is not usable as an id.

## Phases

**1 — Backend.** `Browse` + two passthroughs + `Browse.cs` + two DTOs + the one fact + `API.md`.
Verifiable on its own with `curl 'localhost:5000/Audio/Browse'` and one nested path.

**2 — Frontend.** `getBrowse`, the route, `FolderRow`, the home link.

**3 — Only when asked.** `?path=` deep links; "queue this whole folder" (a `Browse?path=…&recursive=true`
away, and the obvious next want); virtualization for a folder with hundreds of files.

## Skipped, and when to add it

- **Tree cache / prefix index** — linear scan per request; add when the library passes six figures.
- **Breadcrumbs and URL state** — add the moment someone wants to share a folder link.
- **Folder search, sorting options, "play whole folder"** — add on the first real ask; each is
  additive and none changes the endpoint's shape.
