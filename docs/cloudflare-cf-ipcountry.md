# Configuring Cloudflare to inject `CF-IPCountry`

This document explains how to make sure the requests reaching your API through Cloudflare include the `CF-IPCountry` header (ISO2 country code), and good practices for using that header safely.

**Summary**
- Cloudflare can automatically add the `CF-IPCountry` header with the visitor's country code (ISO 3166-1 alpha-2).
- There is no need to send the user's IP from the browser — the CDN injects the country information without exposing the IP to the application's storage.

**Quick steps (per site)**
1. Sign in to the Cloudflare Dashboard and select the site.
2. Go to the **Network** section.
3. Enable **IP Geolocation** (Client IP Geolocation). When enabled, Cloudflare adds the `CF-IPCountry` header to the requests it forwards to the origin.

Note: `CF-IPCountry` contains the country code in uppercase, for example `ES`, `US`, `DE`.

Local testing / simulation
- To test without going through Cloudflare you can simulate the header with curl (useful during development):

```powershell
curl -X POST https://api.tu-dominio/analytics/events \
  -H "CF-IPCountry: ES" \
  -H "Content-Type: application/json" \
  -d '{"siteId":"landing-tempoo","type":"pageview","url":"https://example.com","visitorId":"v-1","sessionId":"s-1","timestamp":"2025-11-29T00:00:00Z"}'
```

Advanced examples (Cloudflare Workers)
- If you need to manipulate headers or fill in the country field when it is missing, you can use a Cloudflare Worker that adds or normalizes the header before forwarding to the origin. Minimal example (JS):

```javascript
addEventListener('fetch', event => {
  event.respondWith(handle(event.request));
});

async function handle(request) {
  const headers = new Headers(request.headers);
  // Force uppercase and/or add it if it does not exist
  const originalCountry = headers.get('CF-IPCountry');
  if (!originalCountry) {
    // you can add extra logic here (for example, fallbacks)
    headers.set('CF-IPCountry', 'unknown');
  } else {
    headers.set('CF-IPCountry', originalCountry.toUpperCase());
  }

  const proxied = new Request(request, { headers });
  return fetch(proxied);
}
```

Security: trust the header only when it comes from Cloudflare
- Important: an attacker can send a request directly to the origin with a forged `CF-IPCountry` header. To mitigate this:
  - Protect the origin so it only accepts traffic from Cloudflare's IP ranges (the list published by Cloudflare). This is usually done in the network or in the server firewall.
  - Alternatively, use authentication on the endpoint (for example, `write_key`) and validate that only authenticated requests are accepted for public events.
  - In the AppHost/development infrastructure you can leave the header open, but in production validate where the traffic comes from.

Alternative headers
- Some platforms/proxies use other names, so the worker accepts several fallbacks (the API looks for them):
  - `CF-IPCountry` (Cloudflare)
  - `X-Geoip-Country`
  - `X-Country`

Final recommendations
- Serve the tracking script from the CDN (Cloudflare) so the header is added automatically.
- Do not store the client IP in the database unless you have a legal obligation and retention/security measures. Using `CF-IPCountry` avoids storing the IP.
- For environments without a CDN, consider enrichment in the worker with MaxMind (see the worker README) or injecting the header from a proxy/ingress that you control.
