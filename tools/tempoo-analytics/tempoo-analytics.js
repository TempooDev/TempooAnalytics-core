(function () {
    "use strict";

    // Single source of the public tracker. `npm run build:all` minifies it into dist/analytics.js (published
    // to the CDN) and copies it into the Angular app's assets folder (`npm run sync`).

    // ========= CONFIGURATION AND CHECKS =========

    // Avoid loading the script more than once and duplicate listeners
    if (window.tempoo && window.tempoo.loaded) {
        return;
    }

    var script = document.currentScript || document.querySelector('script[data-site-id]');

    if (!script) {
        return;
    }

    var config = {
        siteId: script.getAttribute("data-site-id") || script.getAttribute("data-site"),
        writeKey: script.getAttribute("data-key"),
        apiUrl: script.getAttribute("data-api-url") || (script.src ? new URL(script.src).origin + "/analytics/events" : ""),
        debug: script.getAttribute("data-debug") === "true",
        honorDNT: script.getAttribute("data-honor-dnt") !== "false",
        heatmaps: script.getAttribute("data-heatmaps") === "true"
    };

    // ========= HEATMAP OVERLAY MODE =========
    // The site owner opens the page from the app with #tempoo-heatmap=<token>. That tab records nothing:
    // it only loads heatmap-overlay.js from the same CDN folder, which draws the heatmap over the page.

    var overlayToken = /[#&]tempoo-heatmap=([^&]+)/.exec(location.hash);
    if (overlayToken) {
        window.tempoo = { loaded: true, track: function () { }, pageview: function () { } };
        var overlay = loadSibling("heatmap-overlay.js");
        overlay.setAttribute("data-token", decodeURIComponent(overlayToken[1]));
        overlay.setAttribute("data-api-url", config.apiUrl);
        overlay.setAttribute("data-site-id", config.siteId || "");
        return;
    }

    if (config.honorDNT && (navigator.doNotTrack === "1" || window.doNotTrack === "1")) {
        logDebug("DNT detected");
        return;
    }

    if (!config.siteId || !config.writeKey || !config.apiUrl) {
        return;
    }

    if (location.protocol === "file:" || isLocalhost(location.hostname)) {
        if (!config.debug) return;
    }

    // ========= HELPERS =========

    // Loads another file published next to this script on the CDN.
    function loadSibling(name) {
        var el = document.createElement("script");
        el.src = (script.src || "").replace(/[^\/?#]*([?#].*)?$/, name);
        el.async = true;
        document.head.appendChild(el);
        return el;
    }

    function logDebug(msg, data) {
        if (!config.debug) return;
        console.log("[TempooAnalytics]", msg, data || "");
    }

    function isLocalhost(host) {
        return ["localhost", "127.0.0.1", "::1"].includes(host) || host.endsWith(".local");
    }

    function uuid() {
        try {
            return crypto.randomUUID();
        } catch (e) { /* insecure context or old browser: fallback */ }
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = Math.random() * 16 | 0, v = c == 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }

    // UUID v7: 48-bit timestamp in ms, then the random part of a v4.
    function uuid7() {
        var t = Date.now().toString(16).padStart(12, "0");
        return t.slice(0, 8) + "-" + t.slice(8) + "-7" + uuid().slice(15);
    }

    // ========= SESSION MANAGEMENT =========
    // Only a per-tab session id in sessionStorage. No persistent visitor id: unique visitors come from the
    // server-side hash.

    var SESSION_KEY = "tempoo_session";

    function getSessionId() {
        try {
            var sid = sessionStorage.getItem(SESSION_KEY);
            if (!sid) {
                sid = uuid();
                sessionStorage.setItem(SESSION_KEY, sid);
            }
            return sid;
        } catch (e) {
            return window._tempoo_mem_sid || (window._tempoo_mem_sid = uuid());
        }
    }

    // ========= PAYLOAD =========

    function collectBase(url) {
        return {
            siteId: config.siteId,
            timestamp: new Date().toISOString(),
            url: url || location.href,
            referrer: document.referrer || null,
            sessionId: getSessionId(),
            visitorId: "",
            viewport: {
                width: window.innerWidth || document.documentElement.clientWidth,
                height: window.innerHeight || document.documentElement.clientHeight
            }
        };
    }

    // ========= TRANSPORT =========

    // url: the page the event belongs to (default: the current one). title: only sent with page views.
    function sendEvent(eventType, eventValue, extraData, url, title) {
        var payload = collectBase(url);

        payload.type = eventType;
        payload.eventName = eventValue;
        payload.title = title;
        // Kept across retries: the server stores an eventId once.
        payload.eventId = uuid7();

        if (extraData) {
            payload.data = extraData;
        }

        post(JSON.stringify(payload), 0);
    }

    var off = 0;

    // Text body and the key in the URL: a simple CORS request (no preflight) whose status can be read.
    // Retries only 429/503 and network errors, with backoff and jitter. While the page is being hidden,
    // sendBeacon instead (it outlives the page; no retries).
    function post(body, attempt) {
        var url = config.apiUrl;
        var authUrl = url + (url.indexOf("?") === -1 ? "?" : "&") + "key=" + encodeURIComponent(config.writeKey);

        if (off) return;
        if (document.visibilityState == "hidden" && navigator.sendBeacon && navigator.sendBeacon(authUrl, body)) return;

        fetch(authUrl, { method: "POST", body: body, keepalive: true }).then(function (r) {
            var s = r.status;
            if (s == 429 || s == 503) retry();
            // Wrong key, trial over, origin not allowed or unknown site: nothing else on this page will work.
            if (s > 400 && s < 405) off = 1;
        }, retry);

        function retry() {
            if (attempt < 3) setTimeout(function () { post(body, attempt + 1); }, 1000 * (1 << attempt) * (0.5 + Math.random()));
        }
    }

    // ========= TRACKING =========

    var lastUrl = location.href;
    var viewStart = Date.now();
    var maxScroll = 0;
    var scrollSent = false;
    var scrollFrame = 0;

    // lastUrl is the page of the current page view: its leave is sent with that URL even when the browser has
    // already moved to the next one (Back/Forward fire popstate after location changes).
    function trackPageview() {
        lastUrl = location.href;
        sendEvent("pageview", null, null, lastUrl, document.title);
        viewStart = Date.now();
        maxScroll = 0;
        scrollSent = false;
        trackScroll();
        notifyHeatmap("view");
    }

    // Maximum share (0-100) of the document height that reached the bottom of the viewport.
    function trackScroll() {
        var doc = document.documentElement;
        var height = Math.max(doc.scrollHeight, document.body ? document.body.scrollHeight : 0);
        if (!height) return;
        var seen = Math.min(100, Math.round(((window.scrollY || doc.scrollTop) + (window.innerHeight || doc.clientHeight)) / height * 100));
        if (seen > maxScroll) maxScroll = seen;
    }

    // Sent when the page is hidden or the route changes, with the time on page. The scroll depth goes only
    // with the first leave of each page view, so a visitor switching tabs counts once in the scroll map.
    function trackLeave() {
        var duration = Date.now() - viewStart;
        trackScroll();
        if (duration > 0) {
            var data = { duration: duration };
            if (!scrollSent) {
                data.scroll_depth = maxScroll;
                scrollSent = true;
            }
            sendEvent("leave", null, data, lastUrl);
        }
        notifyHeatmap("leave");
    }

    // Reading the layout on every scroll event is costly: measure at most once per frame.
    window.addEventListener("scroll", function () {
        if (scrollFrame) return;
        scrollFrame = requestAnimationFrame(function () {
            scrollFrame = 0;
            trackScroll();
        });
    }, { passive: true });

    requestAnimationFrame(function () {
        trackPageview();
    });

    var historyPushState = history.pushState;
    if (historyPushState) {
        history.pushState = function (state, title, url) {
            trackLeave();
            var ret = historyPushState.apply(this, arguments);
            if (typeof window.onpushstate == "function") {
                window.onpushstate({ state: state, title: title, url: url });
            }
            window.dispatchEvent(new Event('pushstate'));
            return ret;
        };
    }

    window.addEventListener('popstate', function () {
        if (location.href !== lastUrl) {
            trackLeave();
            trackPageview();
        }
    });

    window.addEventListener('pushstate', function () {
        if (location.href !== lastUrl) {
            trackPageview();
        }
    });

    document.addEventListener("visibilitychange", function () {
        if (document.visibilityState === 'hidden') {
            trackLeave();
        } else {
            viewStart = Date.now();
        }
    });

    window.addEventListener("pagehide", function () {
        if (document.visibilityState !== 'hidden') trackLeave();
    });

    // ========= HEATMAPS (data-heatmaps="true") =========
    // The click collection lives in heatmaps.js, next to this file, so sites without heatmaps keep the
    // small script. It gets the transport and the page view hooks through window.__tempooHeatmaps.

    var heatmapHooks = [];

    function notifyHeatmap(event) {
        heatmapHooks.forEach(function (hook) { hook(event); });
    }

    if (config.heatmaps) {
        loadSibling("heatmaps.js").onload = function () {
            if (typeof window.__tempooHeatmaps === "function") {
                window.__tempooHeatmaps({ send: sendEvent, on: function (hook) { heatmapHooks.push(hook); } });
            }
        };
    }

    // ========= GOLES =========

    function handleGoal(el) {
        var name = el.getAttribute("data-tempoo-goal");
        if (!name) return;

        var data = {};
        Array.from(el.attributes).forEach(function (attr) {
            if (attr.name.startsWith("data-tempoo-goal-")) {
                var key = attr.name.replace("data-tempoo-goal-", "");
                data[key] = attr.value;
            }
        });

        sendEvent("goal", name, data);
    }

    document.addEventListener("click", function (ev) {
        var el = ev.target.closest("[data-tempoo-goal]");
        if (el) handleGoal(el);
    });

    window.tempoo = {
        loaded: true,
        track: function (name, data) {
            sendEvent("custom", name, data);
        },
        pageview: function () {
            trackLeave();
            trackPageview();
        }
    };

})();
