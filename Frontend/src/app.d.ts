// See https://svelte.dev/docs/kit/types#app.d.ts
// for information about these interfaces
declare global {
	namespace App {
		// interface Error {}
		// interface Locals {}
		// interface PageData {}
		interface PageState {
			/** Entries this page has pushed without leaving itself — see `$lib/backWatcher.svelte.ts`. */
			depth?: number;
			/** Which roll the home page is showing — see `$lib/rollHistory`. */
			home?: number;
		}
		// interface Platform {}
	}
}

export {};
