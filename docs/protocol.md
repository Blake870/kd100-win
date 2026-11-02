# Huion Keydial Mini KD100 — USB/HID protocol notes

Everything below was observed on a real KD100 on Windows 11, with the Huion
driver installed but **not running** (no `HuionTablet.exe` / `HuionServer.exe`).
Both the USB cable and the 2.4 GHz USB receiver enumerate as the same device.

| | |
|---|---|
| USB VID / PID | `256C` / `006D` (Huion's generic "tablet" PID) |
| Product string (index 2) | `Huion Tablet_KD100` |
| Manufacturer string (index 1) | `HUION` |
| Firmware string (index 201) | `HUION_T200_210309` |
| String index 202 | `HUION Animation Technology Co.,ltd` |

## Interfaces and HID collections

Windows splits the composite device into these top-level collections
(report descriptors as returned by `HidD_GetPreparsedData`/hidapi):

| Interface | Collection | Usage page / usage | Report ID | Notes |
|---|---|---|---|---|
| MI_00 | — | `FF00` / `01` (vendor) | `0x08` | 12-byte input report (`75 58 95 01` = 88 bits after the ID). **The raw channel.** |
| MI_01 | COL01 | `0D` / `02` (digitizer pen) | `0x0A` | Unused by KD100 (shared Huion firmware). |
| MI_01 | COL02 | `01` / `06` (keyboard) | `0x03` | Factory shortcuts in default mode. Opened exclusively by Windows. |
| MI_01 | COL03 | `0C` / `01` (consumer control) | `0x01` | 16-bit usage, unused in tests. |
| MI_02 | COL01 | `01` / `02` (mouse) | `0x01` | Buttons, X/Y, wheel, AC Pan. Opened exclusively by Windows. |
| MI_02 | COL02 | `FF01` / `00` (vendor) | `0x08` | 5-byte **feature** report only. |
| MI_02 | COL03 | `01` / `0E` (system multi-axis = Windows Radial Controller) | `0x11` | The dial in default mode (behaves like a Surface Dial). |

## Default ("no driver") mode

Right after power-up / enumeration the KD100 behaves like a plain keyboard plus
a Surface-Dial-style radial controller. Nothing is sent on MI_00.

* **Buttons** send hard-coded shortcuts on the keyboard collection, e.g.
  Ctrl+Alt+Z, Space, Ctrl+S, Alt, Shift, Ctrl+D. Holding a key makes the
  firmware repeat the key-down report about every 30 ms.
* **Dial** sends report `0x11` on MI_02 COL03:

  ```
  11 BB RR RR XX XX YY YY WW
     │  └─┬─┘
     │    └─ rotation delta, int16 LE (positive = clockwise), e.g. 01 00, 04 00, fb ff
     └─ bit0 = dial button, bit1 = "touch"/in range (02 = touching, 03 = pressed)
  ```

  Windows turns that into scrolling / the Surface Dial menu.

## Switching to vendor ("driver") mode

Reading **USB string descriptor index 200 (`0xC8`)** switches the device into
vendor mode. This is the same trick the Linux `hid-uclogic` driver uses for
Huion tablets. On Windows this is just:

```c
HidD_GetIndexedString(handle_of_any_KD100_collection, 200, buf, sizeof buf);
```

(the returned string is meaningless: `01 00 00 01`). After that:

* the keyboard, consumer and radial collections go silent: no factory shortcuts
  and no Surface Dial behaviour;
* every button and the dial report on **MI_00, report ID `0x08`**.

No other command (feature report, output report) is needed. The mode is not stored
in the device: after power-up or re-enumeration it starts in default mode again, so a
host app sends the switch on every (re)connect. (kd100-win also watches the keyboard and
radial collections and re-sends the switch if input ever shows up there.) Switching
between the cable and the receiver re-enumerates the device with a new instance path,
and the same switch works on both.

## Vendor-mode input report (MI_00, ID 0x08, 12 bytes)

```
offset  0  1  2  3  4  5  6  7  8  9 10 11
        08 TT 01 01 b0 b1 b2 00 00 00 00 00
```

| Byte | Meaning |
|---|---|
| 0 | Report ID `0x08` |
| 1 | Type: `0xE0` = button state, `0xF1` = dial step |
| 2–3 | Always `01 01` |
| 4–6 | `0xE0`: 19-bit button bitmap, `mask = b0 \| b1<<8 \| b2<<16`. `0xF1`: byte 5 = `01` clockwise, `02` counter-clockwise |
| 7–11 | Always 0 |

### Buttons (`TT = E0`)

The report carries the **full button state** (a bitmap, not events), so
chords work. While a button is held the same state is resent about every 100 ms.
Releasing all buttons sends an all-zero bitmap. Spurious all-zero
reports also appear now and then, so diff each report against the previous one.

Bit numbers follow the physical layout, row-major from top-left. This matches
the numbering in [mckset/KD100](https://github.com/mckset/KD100) (a GPL-3.0 Linux
driver used only as a cross-check; no code taken):

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

| Bit | Byte/bit | Button |
|---|---|---|
| 0–7 | b0 bit 0–7 | buttons 0–7 |
| 8–15 | b1 bit 0–7 | buttons 8–15 (15 = tall key on the right) |
| 16 | b2 bit 0 | button 16 (wide key, bottom-left) |
| 17 | b2 bit 1 | button 17 (small key, bottom-centre) |
| 18 | b2 bit 2 | dial centre button |

### Dial (`TT = F1`)

One report per detent: `08 f1 01 01 00 01 …` = one step clockwise,
`08 f1 01 01 00 02 …` = one step counter-clockwise. There are no rotation
deltas; speed is the time between reports, so a fast spin
sends a report every ~15–20 ms. After a burst of dial steps the device usually
sends one `E0` report with an empty bitmap.

## Captured samples

```
# button 0 down / up
08 e0 01 01 01 00 00 00 00 00 00 00
08 e0 01 01 00 00 00 00 00 00 00 00
# button 15 (tall right key)
08 e0 01 01 00 80 00 00 00 00 00 00
# dial centre button
08 e0 01 01 00 00 04 00 00 00 00 00
# dial clockwise, counter-clockwise
08 f1 01 01 00 01 00 00 00 00 00 00
08 f1 01 01 00 02 00 00 00 00 00 00
```

## Coexistence with the Huion driver

The Huion software (`HuionTablet.exe`, `HuionServer.exe`, plus a virtual
`ROOT\HIDCLASS` "HUION HID" device) also switches the device into vendor mode
and turns MI_00 reports into its own actions. Windows allows several readers
on MI_00, so if both programs run, every button press fires twice. Run
only one of them.
