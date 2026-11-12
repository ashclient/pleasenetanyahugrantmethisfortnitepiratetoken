## Linked counts and retained match display (0.5.0)

The chest overlay reads **current game / all-time tracked / linked all-time tracked**. Unpaired clients show a dash for the linked total. Settings → Linked counts lets one person create a group and share its invite code. Others join using a display name; clients sync automatically every 10 seconds while open. The total includes you and each member's last synced contribution, including offline members. Reopening the same installation replaces its snapshot; it never adds the saved lifetime count twice. Rewards are shared too, in the settings Rewards table. Only display names and aggregate totals leave the PC. Keep invite codes private. Removing a member rotates the invite; closing a group deletes its online records. Pairing identity is stored locally in linked-counts.json: preserve it when upgrading. If you delete it or move to another PC, ask the creator to remove the old member to avoid counting both installations. This is installation-based pairing, not an Epic account login.

The previous game remains on the overlay in the lobby, including replay corrections. The display changes directly to the next game's count on its first chest, after the previous replay check finishes. If the replay is late, new chests still save while the old count is held. If Record Replays is off or its replay cannot be read, use Settings → Show current game or disable replay checking. Lifetime counts keep accumulating throughout.

History tables have independent scrollbars and update rows in place. Times use the Windows timezone. Settings pages and scrolling are checked by the packaged self-test; settings-*.png previews are included in CI artifacts.

Sync service: https://treasure-ledger-link.mlitw002.chatgpt.site (invite/member capability authentication, no public count listing). The source is preserved in the Sites source repository; this app contains the native client and protocol types.

# Treasure Ledger

A small transparent Windows overlay that automatically tracks visible Fortnite chest-opening and treasure-award notifications. Japanese and English HUDs are supported. Counters persist between runs.

## Run

Download **TreasureLedger.exe** from [Releases](https://github.com/ashclient/pleasenetanyahugrantmethisfortnitepiratetoken/releases). Windows 10 version 2004+ / Windows 11, x64. The runtime and native vision library are bundled; no installer is required.

1. Exit any old tracker from its tray menu, then run the new exe.
2. Run Fortnite in **Windowed Fullscreen**, with the HUD and XP notifications visible.
3. Choose **English / Japanese** from the tray menu or history window.
4. The overlay starts click-through. **Alt+T** unlocks dragging and clicks; press it again to lock. Right-click while unlocked for settings. The tray menu always remains available.

The chest icon shows **this match / total tracked**. Gizmo, loading-screen and music icons show total tracked awards; other nonzero reward categories appear automatically. The background is transparent and counts wrap as they grow.

## Live counting

The live detector normalizes the screen to 1280 × 720 and analyzes a 360 × 202 XP-popup region using OpenCV template correlation. It follows popup content across frames: added chest lines and multiplier changes (X2/X3) can add an opening without waiting for the entire popup to disappear. Static text, sprite-level lines, and multiplier flicker must not add counts. Frames are sampled independently of Windows OCR, targeting about 15 frames/second; actual cadence depends on capture and CPU time.

English is the default for new installs; existing saved language choices remain unchanged.

A dedicated focused OCR backup reads English/Japanese rare-chest names plus nearby XP independently of the glyph template/color. Stable evidence is retained until the toast ends, then added only if the visual detector did not handle the popup. This can delay a backup count by a few seconds. It is conservative on mixed stacks and is not a general sprite-XP counter: ammo boxes and unrelated XP must not count as chests. Actual Japanese rare-chest gameplay still needs validation.

Windows OCR runs separately for match banners and reward names. Reward events require a specific found/received award heading plus a nearby category inside the focused award region from the same sample. Full-screen/menu text, generic Japanese XP/acquired text and ambiguous multi-category clusters are excluded. A clean award area must be seen before the first production drop is counted. Repeated gauntlet/back-bling unlocks are suppressed across app restarts; unidentified repeats are conservatively ignored. Existing saved false entries are not silently deleted: use **History & settings → Review rewards** to exclude or restore individual reward records without affecting chest totals. Known gizmo names include Portable Extractor, Lucky/Sprite Locator, Extraction Site Booster, Extraction Accelerator, and Golden Firework, with Japanese aliases. An award banner is required: merely seeing or equipping a gizmo does not count it. Gizmo counting does not depend on the icon above its category label. Not every localized variant has a real screenshot test yet.

Japanese OCR is needed for Japanese reward text. If unavailable, the history window explains the fallback; the live Japanese chest detector can still run. Install the Japanese Windows OCR optional feature, then select **Retry OCR** for other Japanese rewards.

**Counts remain estimates from captured visible notifications.** This cannot recover old lifetime chests, unseen or disabled notifications, or openings while the app is closed. Nonstandard HUD sizes/aspect ratios, stacked openings too fast to sample, capture restrictions, game updates and recognition errors can cause misses or false counts. **The on-screen match count retains the completed game in the lobby, receives its replay corrections, and switches after the next game’s first chest.** Detection requires lobby navigation tabs near the top and a Play/Ready panel below, in two fresh samples. English and Japanese labels are supported. A Return to lobby menu command alone does not reset the counter. The finished match remains in history and lifetime totals; late replay checks update the held game without resuming active lobby tracking. Match boundaries also use a dedicated waiting/bus header reader plus result banners. During gameplay, two fresh pregame samples confirm a new match; waiting and bus screens stay in the same match, including short games. Slow reward OCR and chest-only frames do not interrupt that confirmation. Delayed OCR from an earlier screen is ignored. Starting mid-game produces a partial match; restarting the tracker resumes the unfinished match. First seeing an X3 banner does not invent three historical openings.

The app does not read game memory, inject code, modify Fortnite, intercept traffic, or send screen images to a service. It only captures Fortnite while its game window is foreground. This is an unofficial app; no live-game accuracy guarantee is made. Completed-replay checks now supplement live chest counts. Existing history is preserved; automatic replay checking starts with matches completed after the first launch of v0.4. Older matches are not imported automatically.

## Destroyed chests and replay checks

Turn on **Fortnite Settings → Game → Replays → Record Replays**. The app checks `%LOCALAPPDATA%\FortniteGame\Saved\Demos` automatically. Change the folder or disable checking in the tray menu. No account connection or manual import is needed. Future completed matches can be checked even if the overlay missed their notifications.

A separate bundled reader uses [LED-N's replay parser](https://github.com/LED-N/Fortnite-ChestCounter/tree/1f387987737cfd606611d5b8d294d5db04af9eab) to identify the recording player and containers opened or destroyed by that player, including the parser's support-destruction/explosion path. It reads completed replay files only. The parser’s unspecified-kind UTC timestamps are explicitly treated as UTC, avoiding a second local-time conversion. Encrypted replays usually lack the usable key until recording ends, so destroyed chests **are added after the match, not immediately**. File writing, parsing and the 15-second polling interval add delay.

Only the replay-total deficit is added to the matching saved match, rather than adding the entire replay or every destroyed chest again. History's Broken column shows the replay's destroyed count. A session is applied once even across restarts. Rewards keep using live detection. Empty waiting-room records no longer override real chest-event evidence. Fully contained populated fragments may be checked as one group while preserving their rows. Histories mixing events from different games, missing player identity, unreadable files and unsupported game versions do not change counts. A replay total below the saved live total is flagged for review; live history is not deleted. Mid-game app restarts and overwritten/missing replay files can still leave gaps.

The worker is an independent GPL-3.0-or-later command-line program using MIT parser code and the GPL OozSharp decompressor. Releases include **ReplayWorkerSource.zip** with its exact complete corresponding source and notices; the tray's Replay worker source & licenses action also extracts it locally. See [worker build instructions](replay-worker/README.md). Main/worker communication uses a file path and JSON; no game process access or replay modification occurs.

Validation covers reconciliation, deduplication, retained history, worker hashing/extraction/launch/parser construction and the existing visual regressions. **Actual encrypted replay parsing and destroyed-chest attribution have not been verified against a completed replay from this user's PC yet.** Check history/settings for replay status and `replay-errors.log` for parse failures. A completed `.replay` would let us validate the full path.

## After a missed notification

Open history/settings from the tray and select **Save diagnosis**, preferably within about 20 seconds. A bounded memory buffer retains the preceding popup masks and reward-banner crops, so you do not need a screenshot timed to the popup. Leaving Fortnite to open settings preserves that buffer.

Saving writes these local files into `%LOCALAPPDATA%\TreasureLedger\Diagnostics`:

- `live-trace.json`: recent popup masks, frame timing, detector decisions, reward-banner images, low-resolution screen context and timestamped OCR samples.
- `replay-diagnosis.json`: latest replay summary, matching decision and saved match/event time ranges, when a replay has been checked.
- `recognition.json`: selected language, latest OCR text and status.
- `game-frame.png`: the last full captured game frame.

Nothing is uploaded automatically. The full game frame, low-resolution context and OCR can contain visible player names; inspect files before sharing them.

History is `%LOCALAPPDATA%\TreasureLedger\ledger.json`; the previous save is `ledger.json.bak`. Preferences are in `preferences.json`. Unreadable history stops startup rather than replacing it with zeroes. Back up these files to move computers.

## Build and validation

```
# Checkout pinned worker dependency under external/chestreader (see workflow).
dotnet publish replay-worker/ReplayWorker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o worker-dist
python scripts/package_replay_worker.py
dotnet run --project tests/TrackerTests.csproj -c Release
dotnet publish src/TreasureLedger.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o dist
```

GitHub Actions runs parser/state/persistence tests and the packaged exe's `--self-test`. `live-regression.txt` reports this build's actual totals and per-frame times on recorded English popup sequences (X2/X3 stacks, added lines, vault runs, static-popup and sprite-level/weakpoint negatives). It also tests supplied Japanese screenshot samples. Packaged smoke tests exercise transparency, large-number layout, click-through, Alt+T and Windows OCR. These checks are separate from live capture validation on a user's PC.

English templates, temporal tracking rules and packed recorded test masks are adapted from [Gabriel Gomez's Fortnite Visual Chest Counter](https://github.com/GabrielGzS/fortnite-visual-chest-counter), MIT. See `THIRD_PARTY_GABRIEL_GOMEZ.txt`. Native vision uses OpenCvSharp/OpenCV. [Icon/reference sources](src/Assets/SOURCES.md) list the bundled Fortnite artwork and anonymous notification crops. Full user screenshots are not bundled. Not affiliated with Epic Games.
