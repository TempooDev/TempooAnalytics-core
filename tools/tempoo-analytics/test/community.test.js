// The community copy of the tracker (dist/community, from scripts/build-community.mjs) never defaults to a hosted service:
// without data-api-url it posts to the origin it was loaded from.
import { describe, it, expect } from "vitest";
import { JSDOM } from "jsdom";
import { readFileSync } from "fs";
import path from "path";
import { fileURLToPath } from "url";

const dist = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../dist/community");
const files = ["analytics.js", "heatmaps.js", "heatmap-overlay.js"];
const code = Object.fromEntries(files.map(f => [f, readFileSync(path.join(dist, f), "utf8")]));

function run(scriptSrc, attrs = "") {
    const dom = new JSDOM(
        `<!doctype html><html><head><script ${scriptSrc ? `src="${scriptSrc}"` : ""} data-site-id="site" data-key="wk" ${attrs}></script></head><body></body></html>`,
        { url: "https://example.com/precios", runScripts: "outside-only", pretendToBeVisual: true }
    );
    const win = dom.window;
    const fetches = [];
    win.fetch = url => { fetches.push(url); return Promise.resolve({ status: 202 }); };
    win.eval(code["analytics.js"]);
    return { win, fetches, wait: () => new Promise(r => setTimeout(r, 50)) };
}

describe("community tracker", () => {
    it("names no hosted service anywhere", () => {
        for (const f of files) expect(code[f]).not.toMatch(/tempooanalytics\.(eu|com)/i);
    });

    it("posts to the origin that served the script when the tag has no data-api-url", async () => {
        const t = run("https://stats.my-instance.test/assets/analytics.js");
        await t.wait();
        expect(t.fetches.length).toBeGreaterThan(0);
        expect(t.fetches[0]).toMatch(/^https:\/\/stats\.my-instance\.test\/analytics\/events\?key=wk/);
    });

    it("honours data-api-url over the derived origin", async () => {
        const t = run("https://stats.my-instance.test/assets/analytics.js", 'data-api-url="https://other.test/analytics/events"');
        await t.wait();
        expect(t.fetches[0]).toMatch(/^https:\/\/other\.test\/analytics\/events\?key=wk/);
    });

    it("sends nothing when the tag is inline and has no API URL to derive (never falls back to a production host)", async () => {
        const t = run(null);
        await t.wait();
        expect(t.fetches).toHaveLength(0);
    });
});
