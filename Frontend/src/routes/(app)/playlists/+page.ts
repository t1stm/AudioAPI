import type { PageLoad } from "./$types"
import { getPublicPlaylists } from "$requests/playlists"

export const load: PageLoad = ({ fetch }) => {
  // Yours needs the token, which only exists in the browser — the page asks for that
  // itself once the account state has loaded. Neither is awaited here: the grid draws
  // its cards as placeholders and swaps them for the real ones.
  return { shared: getPublicPlaylists(fetch).catch(() => []) }
}
