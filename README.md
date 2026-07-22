# Fennec
Cross-platform [[Matrix]](https://matrix.org/) Client in C# / [.NET MAUI](https://dotnet.microsoft.com/en-us/apps/maui) using [Matrix-SDK-Rust](https://github.com/matrix-org/matrix-rust-sdk)

## Natives
Fennec uses UniFFI bindings to matrix-sdk-rust to communicate with your homeserver. Because I couldn't be bothered documenting building natives right now, natives are included in the repository.

Bindings are generated with [uniffi-bindgen-cs](https://github.com/NordSecurity/uniffi-bindgen-cs), which had some minor manual edits.

Later on, natives will be built with GitHub actions, and added to .gitignore.

## Platform support
As of right now, support has been tested and confirmed for the following platforms:
| Platform | Status | Notes |
|--------|--------|--------|
| Windows x64 | Working ✅ |   |
| Android Arm64 | Working ✅ |   |
| Android x64 | Untested ❓ | Might work, binaries are included |
| Windows Arm64 | Unsupported ❌ | Feel free to open a PR to add support! |
| MacOS | Untested ❓ | Feel free to open a PR to add support! |
| iOS | Untested ❓ | Feel free to open a PR to add support! |
| Linux | Unsupported ❌ | Feel free to open a PR to add support! Needs a third party MAUI extension. |

## Building
To build the project, you will need to have [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) installed.

Next, make sure you have the .NET MAUI workload installed. You can do this by running the following command in your terminal:
```bash
dotnet workload install maui
```

> [!IMPORTANT]
> If you want to build for Windows, you will also need to have Visual Studio 2022 or later installed with the .NET MAUI workload. You can download Visual Studio from [here](https://visualstudio.microsoft.com/downloads/).

> [!IMPORTANT]
> If you build for MacOS or iOS, you will need to have Xcode installed. You can download Xcode from the App Store.

> [!IMPORTANT]
> If you want to build for Android, you will need to have Android Studio installed. You can download Android Studio from [here](https://developer.android.com/studio).

After this, you'll want to download the natives for your platform (or all platforms) from [GitHub Actions](https://github.com/Naamloos/fennec/actions/workflows/build-natives.yml)

Extract natives in `Dev.Naamloos.Fennec.Sdk/native`. If you've downloaded only a single specific platform, extract it in `Dev.Naamloos.Fennec.Sdk/native/<platform>`. 
Otherwise, extract it to the folder root. The combined package will have the following structure:

```
natives
├── android-arm64
├── android-x64
├── ios-arm64
├── ios-simulator
├── maccatalyst
├── macos
├── win-x64
```
> [!NOTE]
> Not all of these platforms are supported or planned to be supported, but the binaries are included for convenience.

Finally, you can build the project by running the following command in your terminal:
```bash
dotnet build
```

## Screenshots
Soon, whenever I feel like the UI is presentable.

## Spec compliance
I will be tracking feature support in the following table. This table is based on the [matrix documentation at v1.18](https://spec.matrix.org/v1.18/client-server-api/#summary)

| Feature                          | Desktop/Mobile Required? | Implemented | Notes |
|----------------------------------|--------------------------|-------------|-------|
| Content Repository               | Both                     |             |       |
| Direct Messaging                 | Both                     |             |       |
| Ignoring Users                   | Both                     |             |       |
| Instant Messaging                | Both                     |             |       |
| Presence                         | Both                     |             |       |
| Push Notifications               | Mobile                   |             |       |
| Receipts                         | Both                     |             |       |
| Room History Visibility          | Both                     |             |       |
| Room Upgrades                    | Both                     |             |       |
| Third-party Invites              | Mobile                   |             |       |
| Typing Notifications             | Both                     |             |       |
| User and Room Mentions           | Both                     |             |       |
| Voice over IP                    | Both                     |             |       |
| Client Config                    | Optional                 |             |       |
| Device Management                | Optional                 |             |       |
| End-to-End Encryption            | Optional                 |             |       |
| Event Annotations and reactions  | Optional                 |             |       |
| Event Context                    | Optional                 |             |       |
| Event Replacements               | Optional                 |             |       |
| Read and Unread Markers          | Optional                 |             |       |
| Guest Access                     | Optional                 |             |       |
| Image Packs                      | Optional                 |             |       |
| Moderation Policy Lists          | Optional                 |             |       |
| Policy Servers                   | Optional                 |             |       |
| OpenID                           | Optional                 |             |       |
| Recently used emoji              | Optional                 |             |       |
| Reference Relations              | Optional                 |             |       |
| Reporting Content                | Optional                 |             |       |
| Rich replies                     | Optional                 |             |       |
| Room Previews                    | Optional                 |             |       |
| Room Tagging                     | Optional                 |             |       |
| SSO Client Login/Authentication  | Optional                 |             |       |
| Secrets                          | Optional                 |             |       |
| Send-to-Device Messaging         | Optional                 |             |       |
| Server Access Control Lists      | Optional                 |             |       |
| Server Administration            | Optional                 |             |       |
| Server Notices                   | Optional                 |             |       |
| Server Side Search               | Optional                 |             |       |
| Spaces                           | Optional                 |             |       |
| Sticker Messages                 | Optional                 |             |       |
| Third-party Networks             | Optional                 |             |       |
| Threading                        | Optional                 |             |       |
| Invite permission                | Optional                 |             |       |
| Mutual Rooms                     | Optional                 |             |       |
