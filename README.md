# Treasure Ledger

A small, draggable, always-on-top Windows overlay for automatically counting visible Fortnite chest and treasure notifications. **Japanese HUD is the default; English is selectable.** No counters to click during play.

## Download and run

Download **TreasureLedger.exe** from [Releases](https://github.com/ashclient/pleasenetanyahugrantmethisfortnitepiratetoken/releases). Windows 10 version 2004 or newer / Windows 11, x64. The .NET runtime is bundled. No installer or administrator access is needed.

1. Run the exe and launch Fortnite in **Windowed Fullscreen**. Keep the game foreground and notifications enabled.
2. The overlay is a short transparent strip of actual Fortnite icons and outlined numbers, with no panel or headings. The chest pair is **this match / total tracked**; rewards show totals. Extra nonzero reward categories appear automatically. Counts are measured and wrapped as they grow, including at higher Windows display scaling.
3. The overlay starts **locked and click-through** every launch. Press **Alt+T** to unlock clicks and dragging; press it again to lock. While unlocked, right-click any visible icon/number for history, pause, **日本語 / English**, and exit. The system tray menu remains available while locked. The small dot turns purple while unlocked, green while reading the game, amber while waiting or on an OCR error, and pink while paused.
4. Install the selected language's **OCR** optional feature in Windows Settings if the app reports it is unavailable. This can require Windows administrative approval once. Select **Retry OCR** afterward. The app never silently substitutes English for missing Japanese OCR.

The overlay shows the chest icon with match/total counts, plus gizmo, loading screen and music icons with totals. Other reward icons appear when their count is nonzero. The separate history window breaks out gizmos, loading screens, jam tracks, pickaxes, back bling, vehicles/decals, pirate shop access and gold outfits. Every detected event is saved immediately.

## What the counts mean

**This is an experimental visual tracker, not exact Fortnite telemetry.** It reads visible notifications with local Windows OCR on a 400 ms timer (slower when recognition takes longer). Each frame gets a full-window pass plus an enlarged, high-contrast notification crop so high-resolution Japanese HUD text is not lost to whole-screen downscaling. Japanese OCR word spacing and combined XP/chest lines are normalized. Counts begin while this app is running. It does not retrieve your historical lifetime chests, Epic inventory, or rewards earned while closed. No Epic login is requested.

Japanese Unicode is preserved; Japanese and English notification patterns are included. **Current Japanese Fortnite HUD footage has not been available to validate the exact award wording, layout, or OCR accuracy.** Do not treat a successful build or synthetic test as proof of live accuracy. If an award banner displays only an item's name rather than its category, that reward will not be recognized by these category patterns. UI language remains English; the selector controls the game's OCR language.

Chest notifications must appear in the central HUD. Quest sidebar text and a bare `Gizmo` item name are ignored. The same visible notification is counted once until it has disappeared for at least 1.8 seconds. This avoids repeated counts, but **stacked chest notifications without a clear gap can be undercounted**. Hidden/disabled XP, destroyed chests without a notification, teammates' rewards, occluded game windows, exclusive fullscreen, nonstandard HUD layout/scale, OCR errors and missed start banners can cause missed or incorrect counts. Match boundaries use waiting/bus/result banners; opening mid-game yields a partial match. Restarting mid-game yields another partial record.

Capture pauses while Fortnite is not foreground. The app does not inject, hook, read game memory, inspect credentials, intercept traffic, or send screen images to a server. The overlay requests Windows capture exclusion. This is not an Epic-certified application and no anti-cheat compatibility guarantee is made.

## Data and diagnostics

History: `%LOCALAPPDATA%\TreasureLedger\ledger.json`; the previous save is kept as `ledger.json.bak`. Overlay position/language preferences are in `preferences.json`. An unreadable history file stops startup rather than overwriting it. Back up these files to move computers. The settings window displays the most recent OCR text in memory; the latest game frame and OCR text are held in memory, not automatically written to disk. **Save diagnosis** explicitly writes that frame (`game-frame.png`) and its OCR/decision text (`recognition.json`) into the local `Diagnostics` folder, then opens the folder. This is useful when the app sees Fortnite but misses a chest; it never uploads them automatically. Only the text supporting counted events is saved locally as evidence.

## Build and validation

```
dotnet run --project tests/TrackerTests.csproj -c Release
dotnet publish src/TreasureLedger.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o dist
```

GitHub Actions builds on Windows, runs synthetic Japanese/English counting tests, tests history persistence and duplicate suppression, installs Japanese OCR on the Windows runner, launches the packaged executable with `--self-test`, tests Japanese and English OCR on rendered notifications, checks transparent pixels and large-number layout at 150% scale, exercises click-through and Alt+T registration, renders the overlay, and publishes a prerelease exe plus SHA-256 checksum. Real Fortnite accuracy still needs live-game validation.

Reference APIs: [Windows OCR](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine), [single-file .NET deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview), [Epic Data API scope](https://dev.epicgames.com/documentation/fortnite/using-fortnite-data-api-in-fortnite). Not affiliated with Epic Games.

Bundled Fortnite icon art and source links: [Assets/SOURCES.md](src/Assets/SOURCES.md).
