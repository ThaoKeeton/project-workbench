import react from "@vitejs/plugin-react";
import { fileURLToPath, URL } from "node:url";
import { defineConfig } from "vite";

const root = fileURLToPath(new URL(".", import.meta.url));

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      react: fileURLToPath(new URL("./node_modules/react", import.meta.url)),
      "react/jsx-runtime": fileURLToPath(new URL("./node_modules/react/jsx-runtime.js", import.meta.url)),
    },
  },
  server: {
    host: "127.0.0.1",
    port: 4173,
    strictPort: true,
    fs: { allow: [root, fileURLToPath(new URL("../../plugins/v2/Workbench.TestCases.V2/ui", import.meta.url))] },
    proxy: {
      "/api": {
        target: "http://127.0.0.1:4317",
        ws: true,
      },
    },
  },
  preview: { host: "127.0.0.1", port: 4173, strictPort: true },
});
