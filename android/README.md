# Fixhands for Android Studio

This is the Android Studio project for Fixhands. It packages the existing mobile-first app in an Android WebView, with the app files stored locally in the Android app.

## Open and run

1. Install Android Studio and its Android SDK.
2. In Android Studio, choose **Open** and select this `android` folder.
3. Allow Gradle sync to download Gradle 9.5, the Android Gradle Plugin, Android SDK 36, and AndroidX WebKit.
4. Create or connect an Android emulator/device, then press **Run**.

The project uses Android Gradle Plugin 9.3.3, Gradle 9.5, JDK 17, and Android SDK 36. The Gradle wrapper is included; Android Studio supplies its bundled JDK and can install the SDK components during sync.

## Updating the app content

The website source remains in the project root. After making website changes, run `.\sync-web-assets.ps1` from this folder in PowerShell to copy the latest web app into `app/src/main/assets` before rebuilding the Android app.

New professional registrations are sent to the Fixhands IIS API and saved to `fixhands.dbo.Skilledreg` when the server connection is configured. Set the deployed HTTPS IIS origin in the web root `api-config.js` and run `.\sync-web-assets.ps1` before rebuilding. Customer service requests are sent to `POST /api/requests` and saved in `fixhands.dbo.ServiceRequests`; quotes, payments, and the directory cache remain local to each device. Configure the API URL in `api-config.js` for Android; admin login does not sync the local cache to SQL.
