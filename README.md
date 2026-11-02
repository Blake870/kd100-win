# kd100-win

An open-source replacement for the Huion software for the **Huion Keydial Mini KD100**
(18 keys + dial) on Windows. A small tray app: per-app profiles, gestures, a radial
menu on the dial, a second layer, and a visual settings window.

[Русская версия](README.ru.md)

> Not affiliated with or endorsed by Huion. "Huion" and "Keydial" are trademarks of their owner.

## Features

- **Works over the USB cable and the 2.4 GHz receiver**, reconnects by itself after
  unplugging, receiver sleep or Windows sleep.
- **Per-app profiles** switched automatically by the foreground program (Premiere Pro,
  browsers, anything). Buttons a profile does not set fall back to the default profile.
- **Gestures on every key**: click, double click and hold can each do something different.
- **Radial menu**: hold a key and turn the dial to pick a program, site or action, then
  release the key to run it.
- **Dial modes** (volume, scroll, zoom, frame-by-frame, undo history…) with step and
  acceleration, cycled from the dial button.
- **Layer 2**: hold a key (or toggle it) for a second set of actions and dial modes.
- **Actions**: key shortcuts, key sequences, typing text, opening programs, folders,
  files or links (in the default browser or a chosen one), media and volume keys,
  mouse buttons, scrolling.
- **Settings window** (English / Russian) with a picture of the device: press a key on
  the KD100 and it is selected; shortcuts are recorded by pressing them.
- Settings live in a readable, commented JSON file; edits are applied on save.
- On-screen hints when the profile, dial mode or layer changes. Start with Windows from the tray menu.

## Requirements

- Windows 10 or 11, x64.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64).
- Microsoft Edge WebView2 Runtime for the settings window. Windows 11 already has it.
- **Quit the Huion software** (tray icon → Exit) while using kd100-win. You do not have
  to uninstall it, but if both run, every key fires twice.

## Install

1. Download `kd100-win.exe` from the Releases page, or [build it](docs/building.md).
2. Put it in a folder of your choice, e.g. `%LOCALAPPDATA%\Programs\kd100-win`, and run it.
3. A KD100 icon appears in the tray. Double-click it to open the settings.
   Right-click → **Start with Windows** to run it at sign-in.

The first start writes a default configuration to `%APPDATA%\kd100-win\config.json`.
**Portable mode:** if a `config.json` sits next to `kd100-win.exe`, that file is used instead.

## Using it

Button numbers used everywhere (dial on top, dial centre button = 18 / `"dial"`):

```
        ( dial, centre button = 18 )
  ┌────┬────┬────┬────┐
  │  0 │  1 │  2 │  3 │
  ├────┼────┼────┼────┤
  │  4 │  5 │  6 │  7 │
  ├────┼────┼────┼────┤
  │  8 │  9 │ 10 │ 11 │
  ├────┼────┼────┼────┤
  │ 12 │ 13 │ 14 │    │
  ├────┴────┼────┤ 15 │
  │   16    │ 17 │    │
  └─────────┴────┴────┘
```

The default configuration:

| | Default | Browser | Premiere Pro |
|---|---|---|---|
| Dial | volume (hold the dial button for scroll, zoom, undo history) | scroll, volume, tabs, zoom, seek | frames, edit points, timeline zoom |
| Dial button | click: play/pause, double: next track, hold: next dial mode | same as Default | next dial mode |
| Key 0 | undo; **hold + turn the dial: radial menu** | back; hold + dial: radial menu | selection tool |
| Key 16 | hold for layer 2 | hold for layer 2 | hold for layer 2 |

Everything can be changed in the settings window. While the settings window is in front,
the KD100 does not send any input; it only highlights the key you press.

The tray menu also has an **input monitor** that shows each key and dial step with the action
it ran, plus the log.

## Configuration

The settings window writes the same file you can edit by hand. Comments (`//`) and
trailing commas are allowed. Saving the file applies it immediately; if it has an error,
a notification explains where and the previous settings stay active.

```jsonc
{
  "language": "auto",        // "auto", "en" or "ru"
  "showOsd": true,           // on-screen hints
  "holdMs": 400,             // how long a "hold" takes
  "doubleMs": 300,           // how long to wait for the second press of a "double"
  "menus": {
    "apps": [
      { "label": "YouTube", "action": "run:https://www.youtube.com/feed/playlists" },
      { "label": "Explorer", "action": "run:explorer.exe", "icon": "C:\\path\\to\\icon.png" }
    ]
  },
  "profiles": [
    {
      "name": "Default",                  // the profile without "apps" is the default
      "buttons": {
        "0": { "tap": "ctrl+z", "menu": "apps" },
        "1": "ctrl+shift+z",
        "dial": { "tap": "media_play_pause", "double": "media_next", "hold": "dial:next" }
      },
      "dial": [
        { "name": "Volume", "cw": "volume_up", "ccw": "volume_down", "acceleration": 2 },
        { "name": "Scroll", "cw": "wheel:down", "ccw": "wheel:up", "step": 1 }
      ],
      "layer": { "buttons": { "0": "win+e" }, "dial": [] }
    },
    {
      "name": "Browser",
      "apps": [ "chrome", "msedge", "firefox" ],   // process names, ".exe" optional
      "buttons": { "0": "alt+left" }
    }
  ]
}
```

### Actions

| Action | Meaning |
|---|---|
| `ctrl+shift+z` | Key shortcut. Held down as long as the KD100 key is held. |
| `keys:ctrl+a ctrl+c` | Several shortcuts in a row. |
| `text:Hello` | Types the text (any language, independent of the keyboard layout). |
| `run:notepad.exe` | Starts a program, opens a folder, file or URL. Quote paths with spaces: `run:"C:\Program Files\App\app.exe" --arg`. |
| `run:https://…` | Opens a link in the default browser. `run:chrome https://…` uses a specific browser. |
| `wheel:down` | Scrolls (`up`, `down`, `left`, `right`). `wheel:down:40` scrolls less; 120 is one notch. |
| `mouse:left` | Mouse button (`left`, `right`, `middle`, `back`, `forward`), held while the key is held. |
| `media_play_pause` | Media keys: `media_next`, `media_prev`, `media_stop`, `volume_up`, `volume_down`, `volume_mute`. |
| `dial:next` | Next dial mode. Also `dial:prev` or a mode name, e.g. `dial:Zoom`. |
| `layer` | Layer 2 while held. `layer:toggle` switches it on and off. |
| `none` | Nothing. |

Key names: `a`–`z`, `0`–`9`, `f1`–`f24`, `ctrl`, `shift`, `alt`, `win`, `enter`, `esc`, `tab`,
`space`, `backspace`, `delete`, `insert`, `home`, `end`, `pageup`, `pagedown`, `left`, `right`,
`up`, `down`, `minus`, `equals` (also `plus`), `[`, `]`, `;`, `'`, `comma`, `period`, `slash`,
`backslash`, `backtick`, `num0`–`num9`, `add`, `subtract`, `multiply`, `divide`, `decimal`,
`capslock`, `printscreen`, `pause`, `menu`, `browser_back`, `browser_forward`,
`browser_refresh`, and the media keys above. Punctuation is matched by its position on a US
keyboard, so shortcuts keep working with any keyboard layout.

### Buttons and gestures

A button is either one action string, or an object:

| Key | When it fires |
|---|---|
| `tap` | Short press. If the button also has `double`, a single tap waits `doubleMs` first. |
| `double` | Two quick presses. |
| `hold` | Held for `holdMs`. Holdable actions (shortcuts, mouse buttons, `layer`) stay down until release. |
| `menu` | Name of a radial menu. Hold the button and turn the dial to open it; release to run the selected item. Releasing without turning the dial is a `tap`. |

A plain string acts immediately on press and stays held while the button is held.

### Dial modes

Each mode has `name`, `cw` and `ccw` actions, `step` (repeats per detent, default 1) and
`acceleration` (extra repeats when spinning fast: `2` means up to 3× at full speed).
The first mode is active at start; `dial:next` cycles through them. Profiles without
their own dial modes use the default profile's. `layer.dial` replaces them while layer 2 is on.

### Radial menus

`menus` maps a name to 2–12 items with `label`, `action` and an optional `icon`
(`.png`, `.ico` or `.exe`). Without an icon, the program's or default browser's icon is used.

## Troubleshooting

- **Every key fires twice**: the Huion driver is still running. Quit it from its tray icon.
- **Nothing happens in one program**: Windows does not let normal programs send input to
  programs running as administrator. Run kd100-win as administrator too if you need that.
  Windows also blocks a few shortcuts from being sent at all (Ctrl+Alt+Del, Win+L).
- **The KD100 types its factory shortcuts (Ctrl+Alt+Z, Space…)**: kd100-win is not running, or
  it has not reached the device yet; it re-sends the mode switch automatically on reconnect.
- **Logs**: `%APPDATA%\kd100-win\kd100-win.log`, or tray → Input monitor.

## How it works

The KD100 starts in a factory mode where it acts as a keyboard plus a Surface-Dial-style
controller. kd100-win reads USB string descriptor 200, which switches the device into
vendor mode. From then on, all keys and the dial report on a vendor HID interface as a bitmap. The app
reads those reports with [HidSharp](https://github.com/IntergatedCircuits/HidSharp) and turns
them into input with `SendInput`. No driver is installed. The protocol is documented in
[docs/protocol.md](docs/protocol.md).

## Building

See [docs/building.md](docs/building.md).

## Credits

- [mckset/KD100](https://github.com/mckset/KD100), a Linux driver for the KD100, was used to
  cross-check the key numbering (no code was taken).
- [HidSharp](https://github.com/IntergatedCircuits/HidSharp) (Apache 2.0) and
  [Microsoft Edge WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2).

## License

[MIT](LICENSE)
