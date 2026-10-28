# FixHands

FixHands is a mobile-first home-services project that connects customers with local repair professionals and helps providers receive service requests.

## Project components

- **Web app** - customer service discovery and request flow at the repository root.
- **ASP.NET Web Forms API** - server-side registration and service request endpoints in `server/`.
- **Android app** - a WebView wrapper and bundled web assets in `android/`.

FixHands is an early-stage prototype. Example professionals and planning figures are not evidence of live customers, completed jobs, or revenue.

## Run the web prototype

Serve the repository root over HTTP, then open the local URL in a browser. The app uses `api-config.js` to derive the API base from the current origin. To use live requests, deploy the ASP.NET API and configure the web app to use its HTTPS origin.

## Configure the ASP.NET API

1. Open `server/` in Visual Studio with the .NET Framework 4.7.2 developer tools.
2. Restore the NuGet packages listed in `server/packages.config`.
3. Copy `server/Web.config.example` to `server/Web.config`.
4. Set the `Fixhands` SQL connection string in the local `Web.config` or secure IIS configuration. Do not commit the real connection string.
5. Apply the required database scripts in `server/` to the intended development database.

`Web.config` is ignored by Git. `Web.config.example` contains only a placeholder.

## Build the Android app

Open `android/` in Android Studio with a compatible Android SDK installed. Android Studio generates `local.properties` for the local machine; that file is excluded from version control. Configure the backend HTTPS URL before using live API features.

## Security

Never commit database passwords, signing keys, access tokens, or private customer data. Keep deployment secrets in protected environment-specific configuration.
