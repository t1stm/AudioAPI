import { defineConfig } from "vitest/config"
import { sveltekit } from "@sveltejs/kit/vite"
import tailwindcss from "@tailwindcss/vite"

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [tailwindcss(), sveltekit()],
  server: {
    open: false,
    host: "0.0.0.0",
  },
  test: {
    globals: true,
    environment: "jsdom",
    mockReset: true,
  },
})
