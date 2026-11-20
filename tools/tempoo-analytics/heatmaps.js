(function () {
    "use strict";

    // Click collection for heatmaps, loaded by analytics.js only when its tag has data-heatmaps="true".
    // Records where people click, never what they type or read: no textContent, values or data-* attributes.
    // At most 50 records per page view. The records not sent yet go out with each leave event (route change,
    // hidden tab), so a visitor who comes back to the tab keeps recording up to the same 50.

    window.__tempooHeatmaps = function (tracker) {
        if (window.__tempooHeatmaps.started) return;
        window.__tempooHeatmaps.started = true;

        var clicks, burst, recorded;

        function resetHeatmap() {
            clicks = [];
            burst = [];
            recorded = 0;
        }

        function flushHeatmap() {
            if (!clicks.length) return;
            tracker.send("heatmap", null, { clicks: clicks });
            clicks = [];
        }

        // Tokens with 4+ consecutive digits or an @ may identify someone (order ids, emails).
        function safeToken(token) {
            return token && !/\d{4}|@/.test(token);
        }

        // tag#id.class1.class2 for up to 4 levels, at most 200 characters.
        function selectorOf(el) {
            var parts = [];
            for (var i = 0; el && el.nodeType === 1 && el !== document.documentElement && i < 4; i++) {
                var part = el.tagName.toLowerCase();
                if (safeToken(el.id)) part += "#" + el.id;
                var classes = (typeof el.className === "string" ? el.className : "").split(/\s+/).filter(safeToken).slice(0, 2);
                if (classes.length) part += "." + classes.join(".");
                parts.unshift(part);
                el = el.parentElement;
            }
            return parts.join(">").slice(0, 200);
        }

        function looksInteractive(el) {
            return el.closest("a,button,[role=button]") || getComputedStyle(el).cursor === "pointer";
        }

        function recordClick(ev) {
            if (!(ev.target instanceof Element)) return;
            var now = Date.now();
            var doc = document.documentElement;
            var record = {
                x: Math.min(1000, Math.round(ev.pageX / Math.max(doc.scrollWidth, 1) * 1000)),
                y: Math.round(ev.pageY),
                w: window.innerWidth,
                s: selectorOf(ev.target),
                k: "c"
            };

            // Rage click: 3+ clicks within 1 s and 30 px. The burst becomes a single "r" record.
            burst = burst.filter(function (b) {
                var dx = b.px - ev.pageX, dy = b.py - ev.pageY;
                return now - b.t < 1000 && dx * dx + dy * dy < 900;
            });
            if (burst.length >= 2) {
                if (burst.some(function (b) { return b.rage; })) return;
                var before = clicks.length;
                clicks = clicks.filter(function (c) {
                    return !burst.some(function (b) { return b.record === c; });
                });
                recorded -= before - clicks.length;
                record.k = "r";
            }
            if (recorded >= 50) return;
            recorded++;
            clicks.push(record);
            burst.push({ t: now, px: ev.pageX, py: ev.pageY, record: record, rage: record.k === "r" });

            // Dead click: an interactive-looking element and nothing happens within 1 s.
            if (record.k === "c" && looksInteractive(ev.target)) {
                var changed = false;
                var href = location.href;
                var observer = new MutationObserver(function () { changed = true; });
                observer.observe(document, { subtree: true, childList: true, attributes: true, characterData: true });
                setTimeout(function () {
                    observer.disconnect();
                    if (!changed && location.href === href && record.k === "c") record.k = "d";
                }, 1000);
            }
        }

        resetHeatmap();

        tracker.on(function (event) {
            if (event === "view") resetHeatmap();
            else flushHeatmap();
        });
        document.addEventListener("click", recordClick, true);
    };
})();
