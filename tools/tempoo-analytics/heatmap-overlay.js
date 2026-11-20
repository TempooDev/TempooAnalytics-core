(function () {
    "use strict";

    // Loaded by analytics.js only when the page URL has #tempoo-heatmap=<token>, that is, when the site owner
    // opens the page from the Tempoo Analytics app. Draws the page's heatmap over the live page.

    var script = document.currentScript;
    if (!script || window.__tempooHeatmapOverlay) return;
    window.__tempooHeatmapOverlay = true;

    var token = script.getAttribute("data-token");
    var siteId = script.getAttribute("data-site-id") || "";
    var apiOrigin;
    try {
        apiOrigin = new URL(script.getAttribute("data-api-url") || (script.src ? new URL(script.src).origin + "/analytics/events" : "")).origin;
    } catch (e) {
        apiOrigin = (script.src ? new URL(script.src).origin : location.origin);
    }

    var es = (navigator.language || "").toLowerCase().indexOf("es") === 0;
    var t = es ? {
        title: "Mapa de calor",
        clicks: "clics",
        expired: "El enlace ha caducado. Vuelve a abrir el mapa desde Tempoo Analytics.",
        error: "No se ha podido cargar el mapa de calor.",
        empty: "Todavía no hay clics en esta página para este dispositivo.",
        frustration: "Frustración",
        rage: "rage",
        dead: "dead",
        close: "Cerrar",
        desktop: "Escritorio",
        tablet: "Tablet",
        mobile: "Móvil",
        loading: "Cargando…"
    } : {
        title: "Heatmap",
        clicks: "clicks",
        expired: "This link has expired. Open the heatmap again from Tempoo Analytics.",
        error: "The heatmap could not be loaded.",
        empty: "No clicks on this page for this device yet.",
        frustration: "Frustration",
        rage: "rage",
        dead: "dead",
        close: "Close",
        desktop: "Desktop",
        tablet: "Tablet",
        mobile: "Mobile",
        loading: "Loading…"
    };

    // Wait for the full page (images included) so the document height matches what visitors saw.
    if (document.readyState === "complete") start();
    else window.addEventListener("load", start);

    function start() {
        var device = window.innerWidth < 768 ? "mobile" : window.innerWidth < 1024 ? "tablet" : "desktop";
        var data = null;

        // ========= CANVAS =========

        var canvas = document.createElement("canvas");
        canvas.setAttribute("aria-hidden", "true");
        canvas.style.cssText = "position:absolute;top:0;left:0;pointer-events:none;z-index:2147483646;opacity:.75";
        document.body.appendChild(canvas);

        // Colour ramp for the heat intensity: blue → green → yellow → red.
        var ramp = (function () {
            var c = document.createElement("canvas");
            c.width = 256;
            c.height = 1;
            var ctx = c.getContext("2d");
            var g = ctx.createLinearGradient(0, 0, 256, 0);
            g.addColorStop(0.15, "#2563eb");
            g.addColorStop(0.45, "#16a34a");
            g.addColorStop(0.7, "#facc15");
            g.addColorStop(1, "#dc2626");
            ctx.fillStyle = g;
            ctx.fillRect(0, 0, 256, 1);
            return ctx.getImageData(0, 0, 256, 1).data;
        })();

        function draw() {
            var doc = document.documentElement;
            var width = Math.max(doc.scrollWidth, doc.clientWidth);
            var height = Math.max(doc.scrollHeight, document.body.scrollHeight);
            canvas.width = width;
            canvas.height = height;
            canvas.style.width = width + "px";
            canvas.style.height = height + "px";

            var ctx = canvas.getContext("2d");
            ctx.clearRect(0, 0, width, height);
            if (!data || !data.points || !data.points.length) return;

            var max = 0;
            data.points.forEach(function (p) { if (p.count > max) max = p.count; });
            var radius = 24;

            // Intensity in the alpha channel first, then mapped to the colour ramp.
            data.points.forEach(function (p) {
                var x = p.x / 1000 * width;
                var y = p.y;
                var g = ctx.createRadialGradient(x, y, 0, x, y, radius);
                g.addColorStop(0, "rgba(0,0,0," + Math.max(0.08, p.count / max) + ")");
                g.addColorStop(1, "rgba(0,0,0,0)");
                ctx.fillStyle = g;
                ctx.fillRect(x - radius, y - radius, radius * 2, radius * 2);
            });

            var image = ctx.getImageData(0, 0, width, height);
            var px = image.data;
            for (var i = 3; i < px.length; i += 4) {
                var a = px[i];
                if (!a) continue;
                var o = a * 4;
                px[i - 3] = ramp[o];
                px[i - 2] = ramp[o + 1];
                px[i - 1] = ramp[o + 2];
                px[i] = Math.min(255, a * 2);
            }
            ctx.putImageData(image, 0, 0);
        }

        // ========= PANEL =========

        var panel = document.createElement("div");
        panel.setAttribute("role", "dialog");
        panel.setAttribute("aria-label", t.title);
        panel.style.cssText = "position:fixed;right:16px;bottom:16px;z-index:2147483647;width:300px;max-width:calc(100vw - 32px);" +
            "max-height:60vh;overflow:auto;background:#fff;color:#111827;border:1px solid #e5e7eb;border-radius:12px;" +
            "box-shadow:0 10px 30px rgba(0,0,0,.18);font:13px/1.4 system-ui,-apple-system,Segoe UI,Roboto,sans-serif;padding:12px";
        document.body.appendChild(panel);

        function el(tag, text, css) {
            var e = document.createElement(tag);
            if (text != null) e.textContent = text;
            if (css) e.style.cssText = css;
            return e;
        }

        var highlighted = null;

        function highlight(selector) {
            if (highlighted) highlighted.style.outline = highlighted.__tempooOutline || "";
            highlighted = null;
            try {
                var target = document.querySelector(selector);
                if (!target) return;
                target.__tempooOutline = target.style.outline;
                target.style.outline = "3px solid #dc2626";
                target.scrollIntoView({ block: "center", behavior: "smooth" });
                highlighted = target;
            } catch (e) { /* selector no longer valid on this page */ }
        }

        function render(message) {
            panel.textContent = "";

            var header = el("div", null, "display:flex;align-items:center;justify-content:space-between;margin-bottom:8px");
            header.appendChild(el("strong", t.title + (data ? " · " + data.totalClicks + " " + t.clicks : "")));
            var close = el("button", "×", "border:0;background:none;font-size:18px;cursor:pointer;color:#6b7280");
            close.setAttribute("aria-label", t.close);
            close.onclick = function () {
                panel.remove();
                canvas.remove();
                if (highlighted) highlighted.style.outline = highlighted.__tempooOutline || "";
            };
            header.appendChild(close);
            panel.appendChild(header);

            var devices = el("div", null, "display:flex;gap:4px;margin-bottom:8px");
            ["desktop", "tablet", "mobile"].forEach(function (d) {
                var b = el("button", t[d], "flex:1;padding:4px 6px;border-radius:6px;cursor:pointer;border:1px solid " +
                    (d === device ? "#111827;background:#111827;color:#fff" : "#e5e7eb;background:#fff;color:#111827"));
                b.setAttribute("aria-pressed", String(d === device));
                b.onclick = function () { if (d !== device) { device = d; load(); } };
                devices.appendChild(b);
            });
            panel.appendChild(devices);

            if (message) {
                panel.appendChild(el("p", message, "margin:4px 0;color:#6b7280"));
                return;
            }

            var frustrated = (data.topElements || []).filter(function (e) { return e.rageClicks || e.deadClicks; });
            if (!data.totalClicks) panel.appendChild(el("p", t.empty, "margin:4px 0;color:#6b7280"));
            if (!frustrated.length) return;

            panel.appendChild(el("div", t.frustration, "font-weight:600;margin:8px 0 4px"));
            frustrated.forEach(function (e) {
                var row = el("button", null, "display:block;width:100%;text-align:left;border:0;border-top:1px solid #f3f4f6;background:none;padding:6px 0;cursor:pointer;color:inherit");
                row.appendChild(el("code", e.selector, "display:block;font-size:11px;word-break:break-all"));
                row.appendChild(el("span", e.rageClicks + " " + t.rage + " · " + e.deadClicks + " " + t.dead, "color:#b91c1c;font-size:12px"));
                row.onclick = function () { highlight(e.selector); };
                panel.appendChild(row);
            });
        }

        // ========= DATA =========

        function load() {
            data = null;
            draw();
            render(t.loading);
            fetch(apiOrigin + "/api/public/heatmaps/overlay?token=" + encodeURIComponent(token) + "&siteId=" + encodeURIComponent(siteId) + "&device=" + device)
                .then(function (res) {
                    if (res.status === 401) throw new Error("expired");
                    if (!res.ok) throw new Error("error");
                    return res.json();
                })
                .then(function (json) {
                    data = json;
                    draw();
                    render();
                })
                .catch(function (e) {
                    render(e && e.message === "expired" ? t.expired : t.error);
                });
        }

        var resizeTimer;
        window.addEventListener("resize", function () {
            clearTimeout(resizeTimer);
            resizeTimer = setTimeout(draw, 200);
        });

        load();
    }
})();
