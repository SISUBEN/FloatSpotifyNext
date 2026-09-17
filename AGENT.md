# FloatSpotify Next Agent Guide

## Mission

Maintain the Windows desktop lyric overlay without changing its lightweight architecture. Prefer small, reviewable changes that preserve the existing WPF/MVVM design, playback-source abstraction, lyric-source fallback behavior, and release packaging contract.

## Repository map

- `src/FloatSpotify/` — the .NET 8 WPF application.
  - `Playback/` — playback engines, lyric providers, parsers, caching, matching, and diagnostics.
  - `ViewModels/` — UI state and commands; `OverlayViewModel` is the main coordinator.
  - `Windows/` — XAML views, code-behind, and custom lyric rendering.
  - `Storage/` — settings models and JSON persistence.
- `tests/FloatSpotify.Lyrics.Tests/` — a zero-test-framework executable regression suite, including HTTP fixtures and WPF rendering checks.
- `build/build-release.ps1` — produces all release variants.
- `installer/FloatSpotify.Next.iss` — Inno Setup definitions.
- `artifacts/` — generated test and release output; never treat it as source.

## Development workflow

Use PowerShell from the repository root.

```powershell
# Compile the application
dotnet build src/FloatSpotify/FloatSpotify.csproj -c Release

# Run the full regression suite and write render evidence
dotnet run --project tests/FloatSpotify.Lyrics.Tests -c Release -- artifacts/lyrics-word-sync

# Build distributable archives without requiring Inno Setup
powershell -ExecutionPolicy Bypass -File build/build-release.ps1 -SkipInstallers
```

Run the narrowest useful check while iterating, then run the full regression command before declaring a behavior change complete. Tests may perform WPF pixel checks, so run them in a Windows desktop session.

## Implementation conventions

- Target `net8.0-windows10.0.19041.0`; retain Windows 10 build 17763 as the supported minimum unless a change explicitly revises compatibility.
- Keep nullable reference types enabled and use file-scoped namespaces.
- Follow existing C# naming: PascalCase for types/members, camelCase for locals/parameters, and `_camelCase` for private fields.
- Keep network, playback, storage, and view concerns in their current layers. Add a playback source through `IPlaybackEngine`, `PlaybackSource`, and `PlaybackCoordinator`; add a lyric source through `ILyricsProvider` and `LyricsCoordinator`.
- Thread `CancellationToken` through async and network operations. Preserve source switching, timeout, rate-limit, cache, and fallback behavior.
- Keep UI-bound state changes on the WPF dispatcher. Do not replace the existing WPF/MVVM stack or introduce a dependency for functionality already covered by the BCL.
- Add regression coverage to the existing executable test project. Keep tests deterministic and use fixtures/fake HTTP handlers instead of live services unless the command is explicitly a live diagnostic.
- Never commit Spotify sessions, credentials, local settings, caches, generated binaries, or `artifacts/` output.
- Comments and user-facing text may follow the surrounding Chinese or English context; identifiers remain English.

## Release invariants

- The application version is defined in `src/FloatSpotify/FloatSpotify.csproj`.
- Artifact names follow `FloatSpotifyNext-x86_64-v<version>-<variant>.<extension>`.
- If product or architecture naming changes, update both `build/build-release.ps1` and `installer/FloatSpotify.Next.iss` in the same change.
- Release builds intentionally omit symbols and retain only neutral framework resources. WPF trimming is not enabled.

## Commit messages

Use Conventional Commits as specified in `COMMIT_CONVENTION.md`:

```text
type(optional-scope): imperative summary
```

Keep each commit focused and include tests or documentation with the behavior they describe. Run `tools/setup-git-hooks.ps1` once per clone to enable the repository commit template and validation hook.
