# Fennec notification gateway

This ASP.NET Core service implements Matrix `POST /_matrix/push/v1/notify` and
forwards notifications to Firebase Cloud Messaging. FCM delivers to Android
directly and to iOS through APNs.

The container listens on plain HTTP port `8080`. Point the reverse proxy for
`fennec-notif.naamloos.dev` at `http://127.0.0.1:8080`; the public URL used by
the Matrix homeserver is
`https://fennec-notif.naamloos.dev/_matrix/push/v1/notify`.

## Firebase setup

1. Create one Firebase project.
2. Add an Android app with package name `dev.naamloos.fennec`. Download
   `google-services.json` to
   `Dev.Naamloos.Fennec.App/Platforms/Android/google-services.json`.
3. Add an iOS app with bundle ID `dev.naamloos.fennec`. Download
   `GoogleService-Info.plist` to
   `Dev.Naamloos.Fennec.App/Platforms/iOS/GoogleService-Info.plist`.
4. In Apple Developer, enable Push Notifications for that App ID and create an
   APNs authentication key. Upload the `.p8` key, its Key ID, and your Team ID
   in Firebase Console → Project settings → Cloud Messaging → Apple app
   configuration.
5. In Firebase Console → Project settings → Service accounts, choose
   **Generate new private key**. Store the downloaded JSON as
   `secrets/firebase-service-account.json`; never commit it.

The two mobile configuration files identify the Firebase project but are not
server credentials. The service-account JSON is a privileged secret.

## Environment

The process needs one environment variable:

| Variable | Value |
| --- | --- |
| `GOOGLE_APPLICATION_CREDENTIALS` | Absolute in-container path to the Firebase service-account JSON. Compose sets this to `/run/secrets/firebase_credentials`. |

The Docker image already sets `ASPNETCORE_URLS=http://+:8080`. Compose also
accepts two host-side substitutions: `FIREBASE_CREDENTIALS_FILE` to override
the source secret path and `NOTIFICATION_PORT` to override host port `8080`.

## Run

```powershell
docker compose up -d --build
curl http://127.0.0.1:8080/health
```

The Matrix Push Gateway API intentionally has no HTTP authentication. Keep the
service behind the reverse proxy, apply normal request/rate limits there, and
only restrict source IPs if every supported Matrix homeserver is known.
