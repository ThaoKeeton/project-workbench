import * as esbuild from "esbuild";

await esbuild.build({
  entryPoints: ["renderer.js"],
  bundle: true,
  format: "iife",
  outfile: "app.js",
  legalComments: "none",
});
