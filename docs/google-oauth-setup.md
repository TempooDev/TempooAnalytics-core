# Google OAuth client for the GA4 import

The Google Analytics 4 import (`FeatureFlags:Ga4Import`) needs a Google Cloud OAuth client of type **web application**. Flow and token handling are in `docs/data-import-flow.md`.

## 1. Project and APIs

1. In the [Google Cloud Console](https://console.cloud.google.com/) create (or choose) a project, for example `tempoo-analytics-dev` for development and another one for production.
2. **APIs & Services → Library**: enable
   - **Google Analytics Data API** (`analyticsdata.googleapis.com`): reports (`runReport`).
   - **Google Analytics Admin API** (`analyticsadmin.googleapis.com`): list of properties and creation date.

## 2. Consent screen (Google Auth Platform)

1. **Branding**: app name (Tempoo Analytics), support email, logo, home page `https://<your-domain>`, privacy policy `https://<your-domain>/privacy/` and authorized domain `<your-domain>`.
2. **Audience**: type *External*. While the app is not verified it stays in *Testing* mode: add your account (and those of anyone who will test, max. 100) under **Test users**.
3. **Data access → Add or remove scopes**: add only `https://www.googleapis.com/auth/analytics.readonly` ("See and download your Google Analytics data"). It is a *sensitive* scope: to open it to everyone Google requires verifying the app (a published privacy policy and a video of the flow).

## 3. OAuth client

**Clients → Create client**, type **Web application**:

| Environment | Authorized redirect URI |
|---|---|
| Local (Aspire) | `http://localhost:4200/account/imports/google/callback` |
| Production | `https://<your-domain>/account/imports/google/callback` |

No authorized JavaScript origin is needed: the browser never talks to Google except for the consent screen, and the API exchanges the code.

Save the **client ID** and the **secret**.

## 4. Configuration

### Local (Aspire)
The AppHost fixes the web app port at 4200 and computes `Google:RedirectUri`. With the client ID configured, Aspire asks for the secret as a parameter:

```bash
dotnet user-secrets --project src/TempooAnalytics.AppHost set "Parameters:GoogleClientId" "<id>.apps.googleusercontent.com"
```

```bash
dotnet user-secrets --project src/TempooAnalytics.AppHost set "Parameters:GoogleClientSecret" "<secret>"
```

Without `Parameters:GoogleClientId` the API still starts, logs a warning and the GA4 endpoints respond 503.

To try it out, turn on the flag in `src/TempooAnalytics.Api/appsettings.json` (`"Ga4Import": true`, reloaded live) and do not commit it turned on.

### Production
API environment variables:

```
Google__ClientId=<id>.apps.googleusercontent.com
Google__ClientSecret=<secret>
Google__RedirectUri=https://<your-domain>/account/imports/google/callback
FeatureFlags__Ga4Import=false   # true only once Google has verified the app
```

## 5. Verification

1. With the flag on, on the import page of a paid site click **Connect with Google**.
2. Google's screen must ask only for **"See and download your Google Analytics data"** (if "Google hasn't verified this app" appears, that is expected in testing mode: *Continue*).
3. On returning, the app lists the GA4 properties of the account.

If Google responds `redirect_uri_mismatch`, the registered URI does not match `Google:RedirectUri` exactly (scheme, port and path).
