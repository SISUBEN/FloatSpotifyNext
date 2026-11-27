# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

FloatSpotify Next: a lightweight Windows desktop lyric overlay (.NET 8 WPF, MVVM) with Spotify and YouTube Music playback sources. `AGENT.md` has the fuller contributor guide; the essentials are below.

## Commands

Run from the repo root in PowerShell, in a Windows desktop session (tests include WPF pixel checks).

```powershell
# Build the app
dotnet build src/FloatSpotify/FloatSpotify.csproj -c Release

# Full regression suite (writes render evidence to the given dir)
dotnet run --project tests/FloatSpotify.Lyrics.Tests -c Release -- artifacts/lyrics-word-sync

# Release archives without needing Inno Setup
powershell -ExecutionPolicy Bypass -File build/build-release.ps1 -SkipInstallers

# One-time per clone: enable commit template + commit-msg validation hook
./tools/setup-git-hooks.ps1
```

There is no test framework (no xUnit/NUnit, no single-test filter). `tests/FloatSpotify.Lyrics.Tests/Program.cs` is a console exe using a `Check(bool, name)` helper that throws on failure. Narrower entry points are selected by first CLI arg: `--youtube-timeline`, `--window-probe`, `--i18n-probe`, `--demo [arg]` (interactive lyric window), `--live-lyrics` / `--live-diagnostics` (hit real services; not deterministic). New regression coverage goes in this project, using fixtures/fake HTTP handlers rather than live services.

## Architecture

- `Playback/` holds two pluggable axes, each with a coordinator:
  - Playback sources: `IPlaybackEngine` implementations (`SpotifyPlaybackEngine`, `YouTubeMusicPlaybackEngine` via the Windows system media session, `DemoPlaybackEngine`) selected via `PlaybackSource`, orchestrated by `PlaybackCoordinator`.
  - Lyric sources: `ILyricsProvider` implementations (NetEase, Kugou/KRC, LRCLIB, word-level/TTML) orchestrated by `LyricsCoordinator`, which races sources (basic result shows first, enhanced word-level upgrades asynchronously) and tolerates individual failures. Matching and scoring live in `LyricsMatchScore` / `LyricVersionMatch`; `LyricsCache` and `LyricsHttpPolicy` handle caching and timeout/rate-limit behavior. Parsers (`LrcParser`, `KrcParser`, `TtmlParser`, `PlainLyricsParser`) normalize to `TimedLyric`. Lyric quality degrades word-sync → line-sync → plain text.
  - Add a playback source via `IPlaybackEngine` + `PlaybackSource` + `PlaybackCoordinator`; add a lyric source via `ILyricsProvider` + `LyricsCoordinator`.
- `ViewModels/OverlayViewModel` is the main coordinator between playback, lyrics, settings and the windows.
- `Windows/` has the XAML views (`LyricsWindow` overlay, `ControlsWindow`, `SpotifySetupWindow`) and custom word-sweep rendering in `KaraokeText`.
- `Storage/` is JSON settings persistence (`SettingsStore`, `AppSettings`, `OverlayPlacement`). `Localization/` provides Chinese/English UI strings (`Strings`, `Loc`, `TrExtension`).
- Spotify uses the official Web API with PKCE and a user-supplied Client ID (none ships); session and rate-limit state are persisted by `SpotifySessionStore` / `SpotifyRateLimitStore`.

## Conventions

- Target `net8.0-windows10.0.19041.0`, minimum supported OS build 17763. Nullable enabled, file-scoped namespaces, `_camelCase` private fields.
- Thread `CancellationToken` through all async/network work; preserve source switching, timeout, rate-limit, cache and fallback behavior. UI-bound state changes stay on the WPF dispatcher.
- Don't add dependencies for things the BCL covers, or replace the WPF/MVVM stack.
- Never commit Spotify sessions, credentials, local settings, caches, or `artifacts/` output.
- Commits use Conventional Commits (`type(scope): imperative summary`, ≤72 chars; see `COMMIT_CONVENTION.md`), enforced by `.githooks/commit-msg`.

## Release invariants

- Version lives in `src/FloatSpotify/FloatSpotify.csproj`.
- Artifacts are named `FloatSpotifyNext-x86_64-v<version>-<variant>.<ext>`. If product/arch naming changes, update both `build/build-release.ps1` and `installer/FloatSpotify.Next.iss` together.
- Release builds omit symbols and keep only neutral framework resources; WPF trimming stays off.
