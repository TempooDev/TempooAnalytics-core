// Smoke test of a published copy of the tracker: loads analytics.js from TRACKER_URL (the CDN folder that
// holds analytics.js, heatmaps.js and heatmap-overlay.js) into a real Chromium and checks what it sends.
// The page and the events endpoint are faked with Playwright routes; only the scripts come from the CDN.
//
//   TRACKER_URL=https://cdn.example.test/builds/<sha> node smoke.mjs
//   EXPECTED_DIR=../dist  also checks that the CDN serves exactly the files of that build.
import { chromium } from "playwright";
import { createHash } from "crypto";
import { readFile } from "fs/promises";
import path from "path";

const base = (process.env.TRACKER_URL || "").replace(/\/+$/, "");
if (!base) {
    console.error("TRACKER_URL is required");
    process.exit(2);
}
const expectedDir = process.env.EXPECTED_DIR;

const pageUrl = "https://smoke.tempoo.test/precios";
const apiUrl = "https://smoke.tempoo.test/collect";
const html = `<!doctype html><html><head>
<script src="${base}/analytics.js" data-site-id="smoke-site" data-key="smoke-key" data-api-url="${apiUrl}" data-heatmaps="true"></script>
</head><body style="height:3000px"><button id="buy" data-tempoo-goal="buy">Comprar</button></body></html>`;

const failures = [];
const check = (ok, message) => {
    console.log(`${ok ? "ok  " : "FAIL"} ${message}`);
    if (!ok) failures.push(message);
};
const sha256 = buf => createHash("sha256").update(buf).digest("hex");

const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || undefined });
try {
    const page = await browser.newPage();
    const events = [];
    const scripts = {};

    // sendBeacon requests can't be intercepted reliably, so the tracker falls back to fetch.
    await page.addInitScript(() => { Object.defineProperty(navigator, "sendBeacon", { value: undefined }); });
    await page.route(pageUrl, route => route.fulfill({ contentType: "text/html", body: html }));
    // SERVE_DIR serves the scripts from a local build instead of the CDN, to try the test itself.
    if (process.env.SERVE_DIR) {
        await page.route(`${base}/*`, async route => {
            const name = new URL(route.request().url()).pathname.split("/").pop();
            const body = await readFile(path.join(process.env.SERVE_DIR, name)).catch(() => null);
            return body ? route.fulfill({ contentType: "application/javascript", body }) : route.fulfill({ status: 404, body: "" });
        });
    }
    await page.route(`${apiUrl}*`, route => {
        if (route.request().method() === "POST") events.push(JSON.parse(route.request().postData() || "{}"));
        return route.fulfill({ status: 202, body: "" });
    });
    page.on("response", async res => {
        const name = res.url().startsWith(base + "/") ? res.url().slice(base.length + 1).split(/[?#]/)[0] : null;
        if (name) scripts[name] = { status: res.status(), type: res.headers()["content-type"] || "", body: await res.body().catch(() => null) };
    });

    await page.goto(pageUrl);
    await page.waitForFunction(() => window.tempoo && window.tempoo.loaded, null, { timeout: 15000 }).catch(() => { });
    await page.waitForResponse(res => res.url().endsWith("/heatmaps.js"), { timeout: 15000 }).catch(() => { });
    await page.waitForTimeout(500);

    for (const name of ["analytics.js", "heatmaps.js"]) {
        const s = scripts[name];
        check(s && s.status === 200, `${name} served with 200 from ${base}/ (got ${s ? s.status : "no request"})`);
        if (s) check(/javascript/.test(s.type), `${name} served as JavaScript (got "${s.type}")`);
    }
    check(events.some(e => e.type === "pageview" && e.siteId === "smoke-site"), "pageview sent to the events endpoint");

    await page.click("#buy");
    await page.waitForTimeout(300);
    check(events.some(e => e.eventName === "buy"), "data-tempoo-goal click sent as an event");

    await page.evaluate(() => {
        Object.defineProperty(document, "visibilityState", { value: "hidden", configurable: true });
        document.dispatchEvent(new Event("visibilitychange"));
    });
    await page.waitForTimeout(500);
    const heatmap = events.find(e => e.type === "heatmap");
    check(heatmap && heatmap.data && heatmap.data.clicks && heatmap.data.clicks.length > 0, "heatmap batch with the click sent on leave");

    if (expectedDir) {
        // heatmap-overlay.js is only loaded with an overlay token; without data-token it does nothing.
        const overlayLoaded = page.waitForResponse(res => res.url().endsWith("/heatmap-overlay.js"), { timeout: 15000 }).catch(() => null);
        await page.evaluate(src => { const s = document.createElement("script"); s.src = src; document.head.appendChild(s); }, `${base}/heatmap-overlay.js`);
        await overlayLoaded;
        await page.waitForTimeout(300);
        for (const name of ["analytics.js", "heatmaps.js", "heatmap-overlay.js"]) {
            const local = await readFile(path.join(expectedDir, name));
            const served = scripts[name] && scripts[name].body;
            check(served && sha256(served) === sha256(local), `${name} on the CDN matches the build`);
        }
    }
} finally {
    await browser.close();
}

if (failures.length) {
    console.error(`\n${failures.length} check(s) failed against ${base}`);
    process.exit(1);
}
console.log(`\nSmoke test passed against ${base}`);
