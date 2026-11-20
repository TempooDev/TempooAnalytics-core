# Dashboard notifications

The bell in the header shows each user what finished or failed in the background. It is behind the `FeatureFlags:Notifications` flag (off by default): when off, `/api/notifications/*` responds 404, the bell does not appear and the dashboard channel stores nothing (the "export ready" email is still sent).

## How to add a notification from a new feature

1. **Notify through `INotificationDispatcher`** (`Features/Notifications`). Never write to `notifications.notifications` or send the email directly: describe what happened and choose the channels.

   ```csharp
   await notifications.DispatchAsync(new UserNotification(
       UserId: userId,
       Type: "ai.weekly_digest.ready",
       Severity: NotificationSeverity.Success,
       Title: text.Title,
       Body: text.Body,
       Link: $"/?site={siteId.Value}",
       SiteId: siteId,
       SourceId: digest.Id),
       NotificationChannels.InApp, ct);
   ```

   Channels (`INotificationChannel`, registered in `ServicesExtensions`):
   - `InAppNotificationChannel`: the dashboard inbox. It does nothing when the flag is off.
   - `EmailNotificationChannel`: email to the user's address, with one template per type (today only `export.completed`). For a new type to be sent by email, add its template in `IEmailService` and its `case` in the channel; types without a template are ignored. Anything only the email needs (such as `downloadUrl`) goes in `Data`, never in the dashboard text.
   - A new channel (for example, push) is another implementation of `INotificationChannel` and a new value in `NotificationChannels`.

   Do not confuse it with `IAdminAlertService` (`Infrastructure/Services`), which alerts the Tempoo owner through the bot when a lead or feedback arrives; its messages are in `AdminAlerts`.

2. **Choose a stable `type`** shaped like `area.event` (`import.completed`, `export.failed`, `ai.weekly_digest.ready`). It is stored as is and will later allow translating on the client. Add it to `NotificationTypes`.

3. **Always pass a `SourceId`** when the notification is about something specific (a job, a digest, a report). The dashboard has a unique index `(user_id, type, source_id)`: if the same message arrives twice, the second does nothing. Without a `SourceId` there is no idempotency.

4. **Write the text in `NotificationTexts`**, in English like the rest of the app, with one method per type and its test in `Unit/Notifications/NotificationTextsTests.cs`. The text is stored with the notification: a new type does not need an Angular deployment and the history does not change if you edit the text later. Limits: title 200, body 1000 and link 500 characters (they are truncated).

5. **`Link` is an app route** (`/account/sites/{siteId}/imports`), never an external URL: the bell does not follow links that do not start with `/`. Do not link to signed URLs that expire (for example, an export download).

6. **A failure to notify does not break the process**: each channel logs its error and does not throw, and a failing channel does not prevent the others.

## Who receives it

Only the user given in `UserId`. For imports it is whoever launched it (`jobs.import_jobs.created_by_user_id`) or, for old jobs without that value, the site owner; for exports, `export_jobs.user_id`. `IJobNotifier` resolves the recipient, the site name, the text and the channels for imports and exports (the ready export goes through both dashboard and email).

## Go worker processes

The worker does not write to the table: it publishes to `queue:notifications` and `JobNotificationListener` calls `IJobNotifier`. The message contract is in `docs/data-import-flow.md`. A new type must be added on both sides; the API ignores types it does not know, so the API can be deployed first.

## API

| Endpoint | Response |
|---|---|
| `GET /api/notifications?cursor&limit` | `{ items: NotificationDto[], nextCursor }`, newest first, `limit` 1–50 (20 by default). Invalid cursor → 400 |
| `GET /api/notifications/unread-count` | `{ count }` |
| `POST /api/notifications/{id}/read` | 204; 404 if it does not exist or belongs to another user. If it was already read, the date is kept |
| `POST /api/notifications/read-all` | 204 |

The app polls the counter every 60 s while the tab is visible and when returning to it (`NotificationService` in Angular). There is no real time.

## Retention

`RetentionMaintenanceWorker` deletes daily, in batches of 5,000, notifications older than 90 days (read or not). They are also deleted when the user or the site is deleted (cascading FK).
