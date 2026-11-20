# Tempoo Analytics Script

This is the lightweight Tempoo Analytics script for tracking events on your website.

## Installation

To start using Tempoo Analytics, include the following script tag in the `<head>` or at the end of the `<body>` of your website:

```html
<script 
  src="https://cdn.your-domain.com/tempoo-analytics.js" 
  data-site-id="YOUR_SITE_ID" 
  data-key="YOUR_WRITE_KEY"
  defer>
</script>
```

### Configuration Attributes

- `data-site-id`: (Required) The unique ID of your site in Tempoo Analytics.
- `data-key`: (Required) Your Write Key to authorize sending events.
- `data-api-url`: (Optional) Custom URL of the events endpoint. Defaults to the production URL.
- `data-debug`: (Optional) If set to `"true"`, logs are shown in the browser console.
- `data-honor-dnt`: (Optional) If set to `"false"`, the script ignores the browser's "Do Not Track" preference. Defaults to `"true"`.

---

## Automatic Tracking

By default, the script automatically tracks **Pageviews** on load and on every route change (for SPAs). You do not need to configure anything else for this.

---

## Custom Event Tracking (Goals)

You can send custom events in two ways: declaratively (using HTML attributes) or programmatically (using JavaScript).

### 1. Declarative Method (No Code)

This method is ideal for tracking clicks on buttons or links without writing a single line of JavaScript. Just add the `data-tempoo-goal` attribute to the element you want to track.

#### Basic Example:
```html
<button data-tempoo-goal="signup_click">Sign up</button>
```

#### Example with Additional Data:
You can send additional metadata using the `data-tempoo-goal-` prefix.
```html
<button 
  data-tempoo-goal="purchase" 
  data-tempoo-goal-product="laptop" 
  data-tempoo-goal-price="1200">
  Buy Now
</button>
```

### 2. Programmatic Method (JavaScript)

If you need to fire an event based on complex logic (for example, after a successful form validation), you can use the global `window.tempoo` object.

#### Syntax:
```javascript
window.tempoo.track("event_name", { extra_data: "value" });
```

#### Usage Example:
```javascript
function onSignupCompleted() {
    // Your business logic...
    
    // Track the event
    if (window.tempoo) {
        window.tempoo.track("signup_completed", {
            method: "google",
            user_type: "premium"
        });
    }
}
```

---

## Using Funnels (Conversion Funnels)

All the events you send (both declarative and programmatic) will be available in the Tempoo Analytics dashboard to build **Funnels**.

### How does it work?
When you create a new Funnel on the platform, you can select these events as steps of the funnel. For example:
1. **Step 1**: Pageview (home page).
2. **Step 2**: Custom event `signup_click` (sign-up button).
3. **Step 3**: Custom event `signup_completed` (after validating the form).

This lets you measure exactly where users drop off in your critical processes.

---

## Privacy and Data

Tempoo Analytics is designed around the principle of **Privacy by Design**:

- **100% GDPR Compliant**: We do not store any personally identifiable information (PII) about your users.
- **No Cookies**: We do not use persistent cookies for tracking.
- **Anonymous IPs**: IP addresses are anonymised immediately and are never stored in full.
- **Smart Analysis**: From the events received, the system automatically calculates relevant information such as:
    - Device type and operating system.
    - Bounce rates.
    - Time on page.
    - Geographic location (country/region level).
- **DNT**: We respect the browser's **Do Not Track (DNT)** setting by default.

## Delivery and retries

- Each event carries an `eventId` (UUID v7) generated in the browser. The server stores each `eventId` received in the last few minutes only once, so a retry does not duplicate the event.
- While the page is visible, the event is sent with `fetch` (text body and the key in the URL, no CORS preflight). If the server responds 503 or 429, or the network fails, it is retried up to 3 times with increasing, randomised back-off (≈1, 2 and 4 s) and the same `eventId`. Any other 4xx is never retried.
- While the page is being hidden or closed, the event is sent with `sendBeacon`, which survives the page but does not let you see the response or retry.
- If the server responds 401 (key), 402 (trial expired), 403 (origin not allowed) or 404 (unknown site), the script stops sending events on that page: none of the following ones would be accepted.


---

## Smoke test

To run the smoke test locally against your own build:

```bash
npm run build
cd smoke && npm ci
TRACKER_URL=https://cdn.example.test/x SERVE_DIR=../dist EXPECTED_DIR=../dist node smoke.mjs
```
