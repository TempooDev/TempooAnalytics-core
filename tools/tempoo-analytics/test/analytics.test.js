// Runs the built tracker (dist/analytics.js, from `npm run build`) inside jsdom and inspects what it sends.
import { describe, it, expect } from "vitest";
import { JSDOM } from "jsdom";
import { readFileSync } from "fs";
import { gzipSync } from "zlib";
import path from "path";
import { fileURLToPath } from "url";

const dist = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../dist");
const tracker = readFileSync(path.join(dist, "analytics.js"), "utf8");
const heatmapsModule = readFileSync(path.join(dist, "heatmaps.js"), "utf8");

const sleep = ms => new Promise(r => setTimeout(r, ms));

function load({ attrs = {}, hash = "", dnt = false, body = "", statuses = [] } = {}) {
    const attrText = Object.entries({ "data-site-id": "site", "data-key": "wk", ...attrs })
        .map(([k, v]) => `${k}="${v}"`).join(" ");
    const dom = new JSDOM(
        `<!doctype html><html><head><script src="https://cdn.example.test/analytics.js" ${attrText}></script></head><body>${body}</body></html>`,
        { url: "https://example.com/precios" + hash, runScripts: "outside-only", pretendToBeVisual: true }
    );
    const win = dom.window;
    const sent = [];
    const fetches = [];
    const beacons = [];
    Object.defineProperty(win.navigator, "doNotTrack", { value: dnt ? "1" : null, configurable: true });
    // jsdom's Blob cannot be read back, so keep the JSON body as given.
    win.Blob = class { constructor(parts) { this.body = parts.join(""); } };
    win.navigator.sendBeacon = (url, data) => { sent.push(typeof data === "string" ? data : data.body); beacons.push(data); return true; };
    // fetch answers the next status in `statuses` (an Error rejects, like a network failure), then 202.
    win.fetch = (url, opts) => {
        fetches.push({ url, opts });
        sent.push(opts.body);
        const next = statuses.length ? statuses.shift() : 202;
        return next instanceof Error ? Promise.reject(next) : Promise.resolve({ status: next });
    };
    win.eval(tracker);
    // jsdom does not fetch scripts: run heatmaps.js by hand where analytics.js asked for it.
    const heatmapsTag = win.document.querySelector('script[src$="/heatmaps.js"]');
    if (heatmapsTag) {
        win.eval(heatmapsModule);
        heatmapsTag.onload();
    }

    const events = async () => sent.map(body => JSON.parse(body));
    const click = (selector, x = 100, y = 200) => {
        const el = win.document.querySelector(selector);
        el.dispatchEvent(new win.MouseEvent("click", { bubbles: true, clientX: x, clientY: y }));
    };
    const hide = () => {
        Object.defineProperty(win.document, "visibilityState", { value: "hidden", configurable: true });
        win.document.dispatchEvent(new win.Event("visibilitychange"));
    };
    const show = () => {
        Object.defineProperty(win.document, "visibilityState", { value: "visible", configurable: true });
        win.document.dispatchEvent(new win.Event("visibilitychange"));
    };
    return { win, events, click, hide, show, fetches, beacons };
}

const heatmaps = async t => (await t.events()).filter(e => e.type === "heatmap");

describe("tracker", () => {
    it("sends no heatmap event and loads no heatmap code without data-heatmaps", async () => {
        const t = load({ body: `<button id="buy">Comprar</button>` });
        await sleep(50);
        expect(t.win.document.querySelector('script[src$="/heatmaps.js"]')).toBeNull();
        t.click("#buy");
        t.hide();
        expect(await heatmaps(t)).toHaveLength(0);
    });

    it("sends one batch with every click when the page is hidden", async () => {
        const t = load({ attrs: { "data-heatmaps": "true" }, body: `<main><p class="a">x</p><p class="b">y</p></main>` });
        await sleep(50);
        t.click("p.a", 10, 10);
        t.click("p.b", 300, 400);
        t.click("p.a", 600, 50);
        t.hide();

        const batches = await heatmaps(t);
        expect(batches).toHaveLength(1);
        const clicks = batches[0].data.clicks;
        expect(clicks).toHaveLength(3);
        for (const c of clicks) {
            expect(c.x).toBeGreaterThanOrEqual(0);
            expect(c.x).toBeLessThanOrEqual(1000);
            expect(c.w).toBe(t.win.innerWidth);
            expect(c.k).toBe("c");
        }
        expect(clicks[1].y).toBe(400);
        expect(clicks[0].s).toBe("body>main>p.a");
    });

    it("keeps only the first 50 records of a page view", async () => {
        const t = load({ attrs: { "data-heatmaps": "true" }, body: `<p>x</p>` });
        await sleep(50);
        for (let i = 0; i < 80; i++) t.click("p", (i % 10) * 100, Math.floor(i / 10) * 100);
        t.hide();
        expect((await heatmaps(t))[0].data.clicks).toHaveLength(50);
    });

    it("sends the new clicks after the visitor comes back, up to 50 per page view", async () => {
        const t = load({ attrs: { "data-heatmaps": "true" }, body: `<p>x</p>` });
        await sleep(50);
        for (let i = 0; i < 30; i++) t.click("p", (i % 10) * 100, Math.floor(i / 10) * 100);
        t.hide();
        t.show();
        for (let i = 0; i < 30; i++) t.click("p", (i % 10) * 100, 500 + Math.floor(i / 10) * 100);
        t.hide();

        const batches = await heatmaps(t);
        expect(batches.map(b => b.data.clicks.length)).toEqual([30, 20]);
    });

    it("sends nothing when Do Not Track is on", async () => {
        const t = load({ attrs: { "data-heatmaps": "true" }, dnt: true, body: `<p>x</p>` });
        await sleep(50);
        t.click("p");
        t.hide();
        expect(await t.events()).toHaveLength(0);
    });

    it("drops id and class tokens that look like personal data", async () => {
        const t = load({ attrs: { "data-heatmaps": "true" }, body: `<p id="order-123456" class="btn user@mail.com">x</p>` });
        await sleep(50);
        t.click("p");
        t.hide();
        const selector = (await heatmaps(t))[0].data.clicks[0].s;
        expect(selector).toContain(".btn");
        expect(selector).not.toContain("123456");
        expect(selector).not.toContain("@");
    });

    it("never reads the element text", async () => {
        const t = load({ attrs: { "data-heatmaps": "true" }, body: `<p>Ana García, 600123123</p>` });
        await sleep(50);
        t.click("p");
        t.hide();
        expect(JSON.stringify(await heatmaps(t))).not.toContain("Ana");
    });

    it("turns a burst of clicks into one rage record", async () => {
        const t = load({ attrs: { "data-heatmaps": "true" }, body: `<p>x</p>` });
        await sleep(50);
        for (let i = 0; i < 4; i++) t.click("p", 100 + i, 100);
        t.hide();
        const clicks = (await heatmaps(t))[0].data.clicks;
        expect(clicks).toHaveLength(1);
        expect(clicks[0].k).toBe("r");
    });

    it("records a dead click when an interactive element changes nothing", async () => {
        const t = load({ attrs: { "data-heatmaps": "true" }, body: `<button id="nada">x</button>` });
        await sleep(50);
        t.click("#nada");
        await sleep(1100);
        t.hide();
        expect((await heatmaps(t))[0].data.clicks[0].k).toBe("d");
    });

    it("does not record a dead click when the page reacts", async () => {
        const t = load({ attrs: { "data-heatmaps": "true" }, body: `<button id="ok">x</button><div id="out"></div>` });
        await sleep(50);
        t.win.document.querySelector("#ok").addEventListener("click", () => {
            t.win.document.querySelector("#out").textContent = "hecho";
        });
        t.click("#ok");
        await sleep(1100);
        t.hide();
        expect((await heatmaps(t))[0].data.clicks[0].k).toBe("c");
    });

    it("adds scroll_depth to the leave event even without heatmaps", async () => {
        const t = load({ body: `<p>x</p>` });
        await sleep(50);
        Object.defineProperty(t.win.document.documentElement, "scrollHeight", { value: 2000, configurable: true });
        Object.defineProperty(t.win, "innerHeight", { value: 500, configurable: true });
        t.win.scrollY = 500;
        t.win.dispatchEvent(new t.win.Event("scroll"));
        t.hide();
        const leave = (await t.events()).find(e => e.type === "leave");
        expect(leave.data.scroll_depth).toBe(50);
        expect(leave.data.duration).toBeGreaterThan(0);
    });

    it("sends scroll_depth only with the first leave of a page view", async () => {
        const t = load({ body: `<p>x</p>` });
        await sleep(50);
        t.hide();
        t.show();
        await sleep(5);
        t.hide();
        const leaves = (await t.events()).filter(e => e.type === "leave");
        expect(leaves).toHaveLength(2);
        expect(leaves[0].data).toHaveProperty("scroll_depth");
        expect(leaves[1].data).not.toHaveProperty("scroll_depth");
    });

    it("sends document.title with page views only", async () => {
        const t = load();
        t.win.document.title = "Precios | Ejemplo";
        await sleep(50);
        t.win.tempoo.track("signup");
        t.hide();
        const evs = await t.events();
        expect(evs.find(e => e.type === "pageview").title).toBe("Precios | Ejemplo");
        expect(evs.find(e => e.type === "custom")).not.toHaveProperty("title");
        expect(evs.find(e => e.type === "leave")).not.toHaveProperty("title");
    });

    it("sends the new page title with each SPA page view", async () => {
        const t = load();
        await sleep(50);
        t.win.document.title = "Contacto";
        t.win.history.pushState({}, "", "/contacto");
        const views = (await t.events()).filter(e => e.type === "pageview");
        expect(views).toHaveLength(2);
        expect(views[1].url).toBe("https://example.com/contacto");
        expect(views[1].title).toBe("Contacto");
    });

    it("attributes the leave to the page being left when the visitor presses Back", async () => {
        const t = load();
        await sleep(50);
        t.win.history.pushState({}, "", "/contacto");
        await sleep(5);
        // Back: the browser changes location first, then fires popstate.
        t.win.history.back();
        await sleep(50);
        const evs = await t.events();
        const leaves = evs.filter(e => e.type === "leave");
        expect(leaves.map(e => e.url)).toEqual(["https://example.com/precios", "https://example.com/contacto"]);
        const views = evs.filter(e => e.type === "pageview");
        expect(views.map(e => e.url)).toEqual(["https://example.com/precios", "https://example.com/contacto", "https://example.com/precios"]);
    });

    it("attributes the leave on page hide to the URL of the page view", async () => {
        const t = load();
        await sleep(50);
        // An untracked URL change (replaceState) does not move the leave to another page.
        t.win.history.replaceState({}, "", "/precios?tab=anual");
        t.hide();
        const leave = (await t.events()).find(e => e.type === "leave");
        expect(leave.url).toBe("https://example.com/precios");
    });

    it("sends no event and loads the overlay in heatmap overlay mode", async () => {
        const t = load({ attrs: { "data-heatmaps": "true" }, hash: "#tempoo-heatmap=abc.def", body: `<p>x</p>` });
        await sleep(50);
        t.click("p");
        t.hide();
        t.win.tempoo.track("custom");
        expect(await t.events()).toHaveLength(0);
        const overlay = t.win.document.querySelector('script[src$="heatmap-overlay.js"]');
        expect(overlay.src).toBe("https://cdn.example.test/heatmap-overlay.js");
        expect(overlay.getAttribute("data-token")).toBe("abc.def");
        expect(overlay.getAttribute("data-site-id")).toBe("site");
    });

    it("stores no visitor id in localStorage", async () => {
        const t = load({ attrs: { "data-heatmaps": "true" }, body: `<p>x</p>` });
        await sleep(50);
        expect(t.win.localStorage.length).toBe(0);
        expect(t.win.document.cookie).toBe("");
    });

    it("loads heatmaps.js from the same CDN folder", async () => {
        const t = load({ attrs: { "data-heatmaps": "true" } });
        expect(t.win.document.querySelector('script[src$="/heatmaps.js"]').src).toBe("https://cdn.example.test/heatmaps.js");
    });

    // The landing promises a script under 2 KB; heatmaps add at most 1 KB, only for sites that turn them on.
    it("keeps analytics.js under 2 KB and heatmaps.js under 1 KB with gzip", () => {
        expect(gzipSync(tracker, { level: 9 }).length).toBeLessThan(2048);
        expect(gzipSync(heatmapsModule, { level: 9 }).length).toBeLessThan(1024);
    });

    it("gives every event its own UUID v7 eventId", async () => {
        const t = load();
        await sleep(20);
        t.win.tempoo.track("signup");
        const evs = await t.events();
        expect(evs.length).toBeGreaterThanOrEqual(2);
        const ids = evs.map(e => e.eventId);
        for (const id of ids) expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
        expect(new Set(ids).size).toBe(ids.length);
        // The first 48 bits are the time in ms.
        const ms = parseInt(ids[0].slice(0, 8) + ids[0].slice(9, 13), 16);
        expect(Math.abs(ms - Date.now())).toBeLessThan(5000);
    });

    it("sends visible-page events with fetch as a simple request, the key in the URL", async () => {
        const t = load();
        await sleep(20);
        expect(t.fetches.length).toBe(1);
        const { url, opts } = t.fetches[0];
        expect(url).toContain("key=wk");
        expect(opts.headers).toBeUndefined();
        expect(opts.keepalive).toBe(true);
        expect(typeof opts.body).toBe("string");
        expect(t.beacons).toHaveLength(0);
    });

    it("retries 503, 429 and network errors with the same eventId, and gives up after 3 retries", async () => {
        const t = load({ statuses: [503, 429, new TypeError("network")] });
        t.win.Math.random = () => 0; // shortest backoff: 0.5 s, 1 s, 2 s
        await sleep(3800);
        expect(t.fetches).toHaveLength(4);
        const ids = t.fetches.map(f => JSON.parse(f.opts.body).eventId);
        expect(new Set(ids).size).toBe(1);

        const down = load({ statuses: [503, 503, 503, 503, 503] });
        down.win.Math.random = () => 0;
        await sleep(3800);
        expect(down.fetches).toHaveLength(4); // 1 + 3 retries
    }, 10000);

    it("never retries a 4xx other than 429", async () => {
        for (const status of [400, 401, 402, 403, 404]) {
            const t = load({ statuses: [status] });
            t.win.Math.random = () => 0;
            await sleep(700);
            expect(t.fetches).toHaveLength(1);
        }
    });

    it("uses sendBeacon while the page is being hidden", async () => {
        const t = load();
        await sleep(20);
        t.hide();
        expect(t.beacons.length).toBeGreaterThanOrEqual(1);
        expect(typeof t.beacons[0]).toBe("string");
        expect(JSON.parse(t.beacons[0]).type).toBe("leave");
    });

    it("stops sending on this page after 401, 402, 403 or 404, but not after 400", async () => {
        for (const status of [401, 402, 403, 404]) {
            const t = load({ statuses: [status] });
            await sleep(20);
            t.win.tempoo.track("signup");
            t.hide();
            expect(t.fetches).toHaveLength(1);
            expect(t.beacons).toHaveLength(0);
        }
        const t = load({ statuses: [400] });
        await sleep(20);
        t.win.tempoo.track("signup");
        await sleep(20);
        expect(t.fetches).toHaveLength(2);
    });
});
