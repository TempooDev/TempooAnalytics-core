// The Angular app serves its own copy of the tracker from src/assets. It is generated from
// tempoo-analytics.js by the build so the two copies cannot drift apart again.
// The community build of the app serves a different copy (src/assets-community, see build-community.mjs) whose
// fallback API is the instance itself instead of the hosted service. The community copy also carries heatmaps.js and
// heatmap-overlay.js: the tracker loads them from its own folder (/assets/ of the instance), where the hosted service has its CDN.
import { promises as fs } from "fs";
import path from "path";
import { fileURLToPath } from "url";

const here = path.dirname(fileURLToPath(import.meta.url));
const app = path.resolve(here, "../../../src/TempooAnalytics/src");
const copies = [
    [path.resolve(here, "../dist/analytics.js"), path.join(app, "assets", "analytics.js")],
    [path.resolve(here, "../dist/community/analytics.js"), path.join(app, "assets-community", "analytics.js")],
    [path.resolve(here, "../dist/community/heatmaps.js"), path.join(app, "assets-community", "heatmaps.js")],
    [path.resolve(here, "../dist/community/heatmap-overlay.js"), path.join(app, "assets-community", "heatmap-overlay.js")],
];

for (const [source, target] of copies) {
    await fs.mkdir(path.dirname(target), { recursive: true });
    await fs.copyFile(source, target);
    console.log(`Copied ${path.relative(process.cwd(), source)} → ${path.relative(process.cwd(), target)}`);
}
