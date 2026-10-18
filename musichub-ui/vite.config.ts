import { resolve } from "node:path"
import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig } from "vite"

// Build identity, set by the image build (Dockerfile); "unknown" for a local build.
const buildValue = (name: string) => process.env[name] || "unknown"

// https://vite.dev/config/
export default defineConfig({
  define: {
    __APP_COMMIT__: JSON.stringify(buildValue("VITE_APP_COMMIT")),
    __APP_BUILD_TIME__: JSON.stringify(buildValue("VITE_APP_BUILD_TIME")),
  },
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": resolve(import.meta.dirname, "./src"),
    },
  },
  server: {
    // Dev only: forward API calls to `dotnet run` (musichub-api, http profile).
    proxy: {
      "/api": "http://localhost:5167",
    },
  },
})
