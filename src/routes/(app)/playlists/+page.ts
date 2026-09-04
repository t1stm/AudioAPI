import type { PageLoad } from "./$types"
import { getPublicPlaylists } from "$requests/playlists"

export const load: PageLoad = async ({ fetch }) => {
  // Yours needs the token, which only exists in the browser — the page asks for that
  // itself once the account state has loaded.
  return { shared: await getPublicPlaylists(fetch) }
}
