# Fixhands

Fixhands is a mobile-first home-services app for finding local professionals and requesting repairs, installations, and maintenance.

## Project components

- Web app at the repository root.
- Android Studio WebView app in `android/`.
- ASP.NET Web Forms backend in `server/`, written in VB.NET.

The directory includes sample professionals. When deployed, professional registration and customer service requests can use the SQL-backed endpoints in the VB.NET site. Quotes, approvals, and receipts are currently stored on the local device.

## Run the web prototype

Serve the repository root over HTTP and open it in a browser. The app uses `api-config.js` to locate the API on the same IIS application. For Android, set the API URL to the deployed HTTPS IIS application because the Android WebView origin cannot infer the server address.

## Configure the VB.NET site

1. Open `server/Fixhands.slnx` or `server/Fixhands.vbproj` in Visual Studio with the .NET Framework 4.7.2 web development tools.
2. Restore the packages listed in `server/packages.config`.
3. Copy `server/Web.config.example` to `server/Web.config` for local development. The real `Web.config` is excluded from Git.
4. Configure `FIXHANDS_SQL_CONNECTION` for the IIS worker process or set the `Fixhands` connection string in protected server configuration. Never commit real SQL credentials.
5. Back up the `fixhands` database, then apply the required SQL scripts in `server/` as described in `server/README-Admin-Integration.md`. Promote only the intended admin account.
6. Deploy the site over HTTPS. Recycle its IIS app pool after changing the connection configuration.

The API supports professional registration and browsing, service requests, private customer-to-professional assignment, and Admin sign-in. Admin access requires `IsAdmin = 1`; legacy plaintext admin passwords are hashed after their first successful login. The dashboard's records and the quote/receipt workflow still use local device storage.

## Build the Android app

Open `android/` in Android Studio with a compatible SDK. Set `window.FIXHANDS_API_BASE_URL` in the root `api-config.js` to the deployed HTTPS IIS URL, run `android/sync-web-assets.ps1`, and build the app. Do not put SQL credentials in the Android project.

## Security

Never commit SQL passwords, signing keys, access tokens, or private customer data. The `.gitignore` excludes server configuration, generated build output, local IDE files, packages, and planning documents.
