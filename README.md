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
| MacOS | Unsupported ❌ | Feel free to open a PR to add support! |
| iOS | Unsupported ❌ | Feel free to open a PR to add support! |
| Linux | Unsupported ❌ | Feel free to open a PR to add support! Needs a third party MAUI extension. |

## Screenshots
Soon, whenever I feel like the UI is presentable.
