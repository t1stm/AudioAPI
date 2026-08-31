<script lang="ts">
	import { goto } from '$app/navigation';
	import { resolve } from '$app/paths';
	import { Icon, PencilSquare } from 'svelte-hero-icons';
	import session from '$states/session.svelte';

	// decorative only — the state word and the rail carry the meaning
	const drops = Array.from({ length: 48 }, (_, index) => index);

	let renaming = $state(false);
	let draft = $state('');
	let title = $derived(session.name || 'Untitled room');
	let holdState = $derived(
		session.status === 'holding' ? 'holding' : session.status === 'synced' ? 'playing' : 'idle'
	);

	function startRename() {
		draft = session.name;
		renaming = true;
	}

	function commitRename() {
		session.rename(draft);
		renaming = false;
	}

	function leave() {
		session.disconnect();
		goto(resolve('/rooms'));
	}
</script>

<div class="strip" data-hold={holdState}>
	<div class="flex min-w-0 items-center gap-3">
		{#if renaming}
			<input
				type="text"
				bind:value={draft}
				onblur={commitRename}
				onkeydown={(event) => {
					if (event.key === 'Enter') commitRename();
					if (event.key === 'Escape') renaming = false;
				}}
				aria-label="Room name"
				class="rounded-row border border-haze bg-dark-0 px-2 py-0.5 text-sm text-chalk"
			/>
		{:else}
			<h1 class="font-display truncate text-sm font-normal text-chalk">{title}</h1>
			<button
				type="button"
				aria-label="Rename this room"
				class="rounded-art p-1 text-fog hover:text-chalk focus-visible:outline-2 focus-visible:outline-primary-200"
				onclick={startRename}
			>
				<Icon src={PencilSquare} mini size="14" />
			</button>
		{/if}
		{#if session.description}
			<span class="hidden truncate text-xs text-fog sm:block">{session.description}</span>
		{/if}
	</div>

	<div class="flex shrink-0 items-center gap-3">
		<span class="font-mono text-[0.72rem] tracking-[0.08em] state">{session.status}</span>
		<span class="hidden font-mono text-[0.68rem] text-fog md:block"
			>here since you joined · {session.roster.length}</span
		>
		<button
			type="button"
			class="rounded-row border border-haze px-2 py-1 text-xs font-semibold text-fog hover:bg-surface-200 hover:text-chalk"
			onclick={leave}
		>
			Leave
		</button>
	</div>

	<div class="rail"></div>
	<div class="drops" aria-hidden="true">
		{#each drops as index (index)}
			<span class="drop" style:--i={index} style:left={(index / (drops.length - 1)) * 100 + '%'}
			></span>
		{/each}
	</div>
</div>

<style>
	/* The hold: between a track change and the release, the strip's bottom hairline
	   breaks into droplets that hang, unfallen. On `playing True` they drop
	   together and the hairline reforms. It is the only animation here. */
	.strip {
		position: relative;
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 1rem;
		overflow: hidden;
		height: 46px;
		padding: 0 1rem;
		background: var(--color-surface-100);
	}

	.state {
		color: var(--color-fog);
		transition: color 0.25s ease;
	}
	[data-hold='holding'] .state {
		color: var(--color-gold);
	}
	[data-hold='playing'] .state {
		color: var(--color-primary-500);
	}

	.rail {
		position: absolute;
		inset-inline: 0;
		bottom: 0;
		height: 1px;
		background: var(--color-haze);
		transition: background-color 0.4s ease;
	}
	[data-hold='holding'] .rail {
		background: color-mix(in srgb, var(--color-gold) 30%, var(--color-haze));
	}
	[data-hold='playing'] .rail {
		background: var(--color-primary-0);
	}

	.drops {
		position: absolute;
		inset: 0;
		pointer-events: none;
	}

	.drop {
		position: absolute;
		bottom: 1px;
		width: 1px;
		height: 10px;
		background: linear-gradient(to bottom, transparent, var(--color-primary-500));
		opacity: 0;
		transform: translateY(-38px);
		transition:
			transform 0.3s cubic-bezier(0.2, 0.7, 0.3, 1) calc(var(--i) * 7ms),
			opacity 0.2s ease calc(var(--i) * 7ms);
	}
	[data-hold='holding'] .drop {
		transform: translateY(-24px);
		opacity: 0.85;
	}
	[data-hold='playing'] .drop {
		transform: translateY(0);
		opacity: 0;
		transition:
			transform 0.32s cubic-bezier(0.45, 0, 0.9, 0.45) calc(var(--i) * 4ms),
			opacity 0.32s linear calc(var(--i) * 4ms);
	}

	/* keep the meaning, drop the movement */
	@media (prefers-reduced-motion: reduce) {
		.drop {
			display: none;
		}
		.rail {
			transition: none;
		}
		[data-hold='holding'] .rail {
			background: repeating-linear-gradient(
				to right,
				var(--color-gold) 0 4px,
				transparent 4px 10px
			);
		}
	}
</style>
