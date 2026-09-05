/// <reference types="@sveltejs/kit" />
// ponytail: install-eligibility only — Chrome needs a fetch handler to offer "Install".
// No precaching, so nothing to invalidate. Add caching here when offline use is wanted.
self.addEventListener('fetch', () => {});
