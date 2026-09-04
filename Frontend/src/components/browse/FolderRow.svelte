<script lang="ts">
	import { ChevronRight, Folder, FolderOpen, Icon } from 'svelte-hero-icons';
	import Self from './FolderRow.svelte';
	import SearchRow from '$components/search/SearchRow.svelte';
	import { getBrowse, type BrowseFolder, type BrowseLevel } from '$requests/songs';

	const { folder }: { folder: BrowseFolder } = $props();
	const contents = $props.id();

	let open = $state(false);
	let level = $state<BrowseLevel | null>(null);
	let loading = $state(false);
	let failed = $state(false);

	/**
	 * One request per folder for the life of the page: a folder that has been opened keeps
	 * what it fetched, so closing and reopening costs nothing and the tree never flickers.
	 */
	async function load() {
		loading = true;
		failed = false;
		try {
			level = await getBrowse(folder.path, fetch);
		} catch {
			failed = true;
		} finally {
			loading = false;
		}
	}

	function toggle() {
		open = !open;
		if (open && !level && !loading) load();
	}
</script>

<div>
	<!-- A real button, so Enter, Space and the focus ring all arrive for free — SearchRow
	     needs its role/keydown pair only because it wraps links of its own. -->
	<button
		type="button"
		aria-expanded={open}
		aria-controls={contents}
		onclick={toggle}
		class="group grid w-full grid-cols-[2.75rem_minmax(0,1fr)_auto] items-center gap-3 rounded-row px-2 py-2 text-left transition-colors hover:bg-surface-100 active:bg-surface-200 focus-visible:bg-surface-100 focus-visible:outline-2 focus-visible:outline-primary-200 sm:gap-3.5 sm:px-2.5"
	>
		<!-- The artwork slot, to the pixel: a folder is a cover you have not opened yet, so
		     folders and tracks share one column and the list reads as one list. -->
		<span
			class="flex size-11 items-center justify-center rounded-art border transition-colors {open
				? 'border-gold/45 bg-gold/10 text-gold'
				: 'border-haze bg-surface-0 text-fog group-hover:text-gold'}"
		>
			<Icon src={open ? FolderOpen : Folder} mini size="20" />
		</span>

		<span class="min-w-0">
			<span class="line-clamp-2 text-sm font-medium leading-snug text-chalk">{folder.name}</span>
		</span>

		<span class="flex items-center gap-2.5">
			<span class="font-mono text-[0.79rem] text-fog"
				>{folder.songs}<span class="sr-only"> {folder.songs === 1 ? 'track' : 'tracks'}</span></span
			>
			<Icon
				src={ChevronRight}
				mini
				size="16"
				class="shrink-0 text-fog transition-transform duration-150 {open ? 'rotate-90' : ''}"
			/>
		</span>
	</button>

	{#if open}
		<!-- The thread. It drops from the centre of the open folder's tile and runs the height
		     of its contents, so every folder you are inside is joined to what it holds — the
		     nested golds are the trail, which is why this page has no breadcrumb. -->
		<div id={contents} class="ml-[1.875rem] border-l border-gold/25 pl-1 sm:ml-8 sm:pl-2">
			{#if loading}
				<p class="px-2 py-2 text-sm text-fog">Opening…</p>
			{:else if failed}
				<p class="flex flex-wrap items-center gap-2 px-2 py-2 text-sm text-fog">
					Could not open this folder.
					<button
						type="button"
						class="rounded-[5px] border border-haze px-2 py-1 text-xs font-semibold text-chalk hover:bg-surface-200 focus-visible:outline-2 focus-visible:outline-primary-200"
						onclick={load}>Try again</button
					>
				</p>
			{:else if level}
				{#each level.folders as child (child.path)}
					<Self folder={child} />
				{/each}
				{#each level.files as file (file.id)}
					<SearchRow result={file} />
				{/each}
				{#if level.folders.length === 0 && level.files.length === 0}
					<p class="px-2 py-2 text-sm text-fog">This folder has no tracks.</p>
				{/if}
			{/if}
		</div>
	{/if}
</div>
