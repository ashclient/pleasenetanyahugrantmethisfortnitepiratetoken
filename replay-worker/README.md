# Independent replay worker

This command-line program reads one completed local Fortnite replay and writes a JSON summary for its recording player to stdout. It never accesses Fortnite's running process or changes a replay. Live and zero-key replays are rejected. Main overlay communication uses a file path and JSON through a separate process.

Source is GPL-3.0-or-later, including the OozSharp dependency. Original MIT notices are retained in the pinned upstream sources. COPYING contains the GPL. UPSTREAM.txt records provenance. Each release includes ReplayWorkerSource.zip containing this directory and the exact upstream checkout under external/chestreader, with all original notices. No internet connection is needed to extract that source from the app's third-party menu.

Build from the extracted source root with .NET 10 SDK:

    dotnet publish replay-worker/ReplayWorker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o worker-dist

Usage:

    TreasureLedger.ReplayWorker.exe path/to/completed.replay

Exit 4 means the file is still live or its key is unavailable. Exit 2 is a parse/error condition. Exit 0 emits a JSON result. --self-test constructs the parser and checks container classification; this is not a recorded-game accuracy test.
