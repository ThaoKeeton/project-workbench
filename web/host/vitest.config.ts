import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      react: new URL("./node_modules/react", import.meta.url).pathname,
      "react/jsx-runtime": new URL("./node_modules/react/jsx-runtime.js", import.meta.url).pathname,
    },
  },
  test: {
    environment: "jsdom",
    include: ["src/**/*.test.ts", "src/**/*.test.tsx"],
    coverage: {
      provider: "v8",
      allowExternal: true,
      reporter: ["lcov", "text"],
      reportsDirectory: "coverage",
      include: ["src/**/*.{ts,tsx}", "../../plugins/v2/Workbench.TestCases.V2/ui/**/*.tsx"],
      exclude: ["**/*.css", "**/*.d.ts", "e2e/**"],
    },
  },
});
