# Treasure Ledger

A small transparent Windows overlay that automatically tracks visible Fortnite chest-opening and treasure-award notifications. Japanese and English HUDs are supported. Counters persist between runs.

## Run

Download **TreasureLedger.exe** from [Releases](https://github.com/ashclient/pleasenetanyahugrantmethisfortnitepiratetoken/releases). Windows 10 version 2004+ / Windows 11, x64. The runtime and native vision library are bundled; no installer is required.

1. Exit any old tracker from its tray menu, then run the new exe.
2. Run Fortnite in **Windowed Fullscreen**, with the HUD and XP notifications visible.
3. Choose **Japanese / English** from the tray menu or history window.
4. The overlay starts click-through. **Alt+T** unlocks dragging and clicks; press it again to lock. Right-click while unlocked for settings. The tray menu always remains available.

The chest icon shows **this match / total tracked**. Gizmo, loading-screen and music icons show total tracked awards; other nonzero reward categories appear automatically. The background is transparent and counts wrap as they grow.

## Live counting

The live detector normalizes the screen to 1280 × 720 and analyzes a 360 × 202 XP-popup region using OpenCV template correlation. It follows popup content across frames: added chest lines and multiplier changes (X2/X3) can add an opening without waiting for the entire popup to disappear. Static text, sprite-level lines, and multiplier flicker must not add counts. Frames are sampled independently of Windows OCR, targeting about 15 frames/second; actual cadence depends on capture and CPU time.

Windows OCR runs separately for match banners and reward names. Known gizmo names include Portable Extractor, Lucky/Sprite Locator, Extraction Site Booster, Extraction Accelerator, and Golden Firework, with Japanese aliases. An award banner is required: merely seeing or equipping a gizmo does not count it. Gizmo counting does not depend on the icon above its category label. Not every localized variant has a real screenshot test yet.

Japanese OCR is needed for Japanese reward text. If unavailable, the history window explains the fallback; the live Japanese chest detector can still run. Install the Japanese Windows OCR optional feature, then select **Retry OCR** for other Japanese rewards.

**Counts remain estimates from captured visible notifications.** This cannot recover old lifetime chests, unseen or disabled notifications, or openings while the app is closed. Nonstandard HUD sizes/aspect ratios, stacked openings too fast to sample, capture restrictions, game updates and recognition errors can cause misses or false counts. Match boundaries use waiting/bus/result banners; starting mid-game produces a partial match. First seeing an X3 banner does not invent three historical openings.

The app does not read game memory, inject code, modify Fortnite, intercept traffic, or send screen images to a service. It only captures Fortnite while its game window is foreground. This is an unofficial app; no live-game accuracy guarantee is made. **Replay import is not included.** Existing historical counts are preserved; previously missed openings are not reconstructed by this update.

## After a missed notification

Open history/settings from the tray and select **Save diagnosis**, preferably within about 20 seconds. A bounded memory buffer retains the preceding popup masks and reward-banner crops, so you do not need a screenshot timed to the popup. Leaving Fortnite to open settings preserves that buffer.

Saving writes these local files into `%LOCALAPPDATA%\TreasureLedger\Diagnostics`:

- `live-trace.json`: recent popup masks, frame timing, detector decisions and reward-banner images.
- `recognition.json`: selected language, latest OCR text and status.
- `game-frame.png`: the last full captured game frame.

Nothing is uploaded automatically. The full game frame can contain visible player names; inspect files before sharing them.

History is `%LOCALAPPDATA%\TreasureLedger\ledger.json`; the previous save is `ledger.json.bak`. Preferences are in `preferences.json`. Unreadable history stops startup rather than replacing it with zeroes. Back up these files to move computers.

## Build and validation

```
dotnet run --project tests/TrackerTests.csproj -c Release
dotnet publish src/TreasureLedger.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o dist
```

GitHub Actions runs parser/state/persistence tests and the packaged exe's `--self-test`. `live-regression.txt` reports this build's actual totals and per-frame times on recorded English popup sequences (X2/X3 stacks, added lines, vault runs, static-popup and sprite-level/weakpoint negatives). It also tests supplied Japanese screenshot samples. Packaged smoke tests exercise transparency, large-number layout, click-through, Alt+T and Windows OCR. These checks are separate from live capture validation on a user's PC.

English templates, temporal tracking rules and packed recorded test masks are adapted from [Gabriel Gomez's Fortnite Visual Chest Counter](https://github.com/GabrielGzS/fortnite-visual-chest-counter), MIT. See `THIRD_PARTY_GABRIEL_GOMEZ.txt`. Native vision uses OpenCvSharp/OpenCV. [Icon/reference sources](src/Assets/SOURCES.md) list the bundled Fortnite artwork and anonymous notification crops. Full user screenshots are not bundled. Not affiliated with Epic Games.
