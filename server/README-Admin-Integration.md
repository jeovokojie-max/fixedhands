# Fixhands VB.NET site and Admin login

The `Fixhands.vbproj` Web Forms site now serves the Fixhands web app from `index.html` and provides the Admin login API on the same IIS origin. `Default.aspx` sends visitors to the app. The Admin page calls `/api/admin/login`, `/api/admin/session`, and `/api/admin/logout`.

## Before deployment

1. Back up the `fixhands` database and run `upgrade-admin-login.sql` in SQL Server Management Studio.
2. Set `IsAdmin = 1` only on the intended administrator's `dbo.TblLogin` row. Existing accounts remain non-admin until explicitly promoted.
3. Configure the server-side `FIXHANDS_SQL_CONNECTION` environment variable for the IIS worker process, or set the `Fixhands` entry in a protected `Web.config`. The real password must stay on the server; the checked-in `Web.config` value is empty intentionally. Recycle the application pool after changing its environment. Grant the SQL identity only `SELECT` and `UPDATE` on `dbo.TblLogin`.
4. Deploy this Web Forms project under HTTPS. Install the applicable IIS ASP.NET Framework 4.7.2 features and restore the NuGet packages in `packages.config`.
5. If the app files in the main project folder change, run `.\sync-web-assets.ps1` from this folder before publishing.

The checked-in `FixhandsAllowedOrigins` setting permits the Android WebView origin. Add any other exact front-end origins as a semicolon-separated list; avoid wildcard origins.

`Web.config` currently sets `<customErrors mode="Off" />` to expose ASP.NET error details while diagnosing the IIS deployment. This can reveal stack traces to remote visitors. Change it to `RemoteOnly` or `On` before exposing the site publicly; the Admin API itself returns sanitized SQL diagnostics.

For the Android WebView build, set `window.FIXHANDS_API_BASE_URL` in the main project's `api-config.js` to the deployed HTTPS URL of this VB.NET IIS application (include its virtual application path if it is not hosted at the site root), then run `android\sync-web-assets.ps1` and rebuild the app. The Android WebView origin cannot infer your IIS address.

The handler recognizes the ASP.NET Identity V3 PBKDF2 hash format used by the earlier VB/C# API. At the first successful login for a legacy plaintext account, it writes `PasswordHash` and replaces `Password` with `!`. The login API accepts only accounts with `IsAdmin = 1`. Sessions are held in memory for eight hours and reset when the app pool restarts.

Professional registrations are submitted to `POST /api/professionals/register` and stored in `fixhands.dbo.Skilledreg`. Phone is stored privately in SQL; public profile IDs are separate GUIDs. `GET /api/professionals/register` returns only the public profile fields and never returns a phone number or street address. Run `upgrade-skilledreg-professionals.sql`, `upgrade-service-requests.sql`, then `upgrade-professional-assignments.sql` against the `fixhands` database. Back up the database before running the new assignment migration. The API uses `FIXHANDS_SQL_CONNECTION` when configured; otherwise it uses the `Fixhands` connection string from `Web.config`. Keep connection strings in protected server configuration or an environment variable, never in browser code. The configured SQL login needs `SELECT, INSERT` permission on `dbo.Skilledreg`, `INSERT` on `dbo.ServiceRequests`, and `SELECT, INSERT` on `dbo.ServiceRequestAssignments`.

The dashboard still displays its browser/device cache. Professional registrations are stored in `fixhands.dbo.Skilledreg`; customer service requests are stored in `fixhands.dbo.ServiceRequests` through `POST /api/requests`. Quotes and receipts remain local to each device. Deploy the updated Web Forms assembly and the synced `index.html`, `app.js`, and `service-worker.js` files for SQL-backed customer requests to work.

Service requests are submitted to `POST /api/requests`. When the customer starts from a live professional profile, the request and a private `dbo.ServiceRequestAssignments` row are committed in one transaction. The customer never receives the professional's phone number. The handler is compiled into `bin/Fixhands.dll`; deploy the updated assembly and synced web assets after applying the SQL migrations.
