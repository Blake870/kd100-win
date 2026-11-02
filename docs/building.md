# Building kd100-win

## Prerequisites

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
  (`winget install Microsoft.DotNet.SDK.10`)

## Build, test, run

```bash
dotnet build
dotnet test
dotnet run --project src/Kd100Win
```

## Release build

```bash
dotnet publish src/Kd100Win -c Release -o publish
```

This produces a single `publish/kd100-win.exe` (about 2 MB) that needs the .NET 10 Desktop
Runtime on the target machine. For a (much larger) exe that runs without the runtime installed:

```bash
dotnet publish src/Kd100Win -c Release -o publish --self-contained true
```

## Project layout

```
src/Kd100Win/
  Program.cs, TrayApp.cs     entry point, tray icon and menu, wiring
  Device/                    finding the KD100, switching it to vendor mode, reading reports
  Engine/                    actions, profiles, gestures, layers, dial modes (no UI, unit-tested)
  Input/                     SendInput wrapper and key names
  Config/                    config.json model, loading, watching
  Platform/                  foreground app detection, autostart, process list
  UI/                        settings window (WebView2 + settings.html), radial menu, OSD, input monitor
tests/Kd100Win.Tests/        xUnit tests for the protocol parser, config and engine
config.sample.json           default configuration (embedded into the exe)
docs/protocol.md             USB/HID protocol notes
```

## Working on the settings page

`src/Kd100Win/UI/settings.html` is a single self-contained page. Opened directly in a
browser, it runs with built-in sample data instead of the app, which is handy for layout work.
Inside the app it talks to `SettingsForm.cs` through `window.chrome.webview.postMessage`.
