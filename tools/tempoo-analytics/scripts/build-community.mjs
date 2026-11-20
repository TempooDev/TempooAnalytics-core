// Builds the self-host copy of the tracker into dist/community (the copy the community frontend serves from src/assets-community).
//
// In this repository the tracker source already has no production host as a fallback: without data-api-url it posts to the origin
// the script was loaded from (the instance serves /assets/analytics.js and /analytics/events from the same gateway), so this build
// only minifies. Same outputs as the build of the private monorepo, which also rewrites a hosted default.
import { promises as fs } from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { transform } from "esbuild";

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, "..");
const outDir = path.join(root, "dist", "community");

async function build(file) {
    const code = await fs.readFile(path.join(root, file), "utf8");
    const { code: minified } = await transform(code, { minify: true, format: "iife" });
    await fs.mkdir(outDir, { recursive: true });
    const target = path.join(outDir, path.basename(file));
    await fs.writeFile(target, minified);
    console.log(`Community ${path.basename(file)} → ${path.relative(root, target)} (${minified.length} bytes)`);
}

await build("tempoo-analytics.js");
await build("heatmaps.js");
await build("heatmap-overlay.js");
await fs.rename(path.join(outDir, "tempoo-analytics.js"), path.join(outDir, "analytics.js"));
