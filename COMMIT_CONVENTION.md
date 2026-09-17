# Commit message convention

FloatSpotify Next uses [Conventional Commits](https://www.conventionalcommits.org/) for human-readable history and predictable release notes.

## Format

```text
<type>(<optional-scope>)<optional-!>: <summary>

<optional body>

<optional footer>
```

The first line must be at most 72 characters, use an imperative summary, and omit a trailing period.

Allowed types:

| Type | Use for |
| --- | --- |
| `feat` | User-visible capability |
| `fix` | User-visible defect correction |
| `docs` | Documentation only |
| `style` | Formatting with no behavior change |
| `refactor` | Internal restructuring with no feature or fix |
| `perf` | Performance improvement |
| `test` | Test additions or corrections |
| `build` | Build system, packaging, or dependencies |
| `ci` | Continuous-integration configuration |
| `chore` | Repository maintenance |
| `revert` | Revert of an earlier commit |

Use a short lowercase scope when it adds useful context, such as `playback`, `lyrics`, `ui`, `storage`, `tests`, `build`, or `installer`. Use `!` and a `BREAKING CHANGE:` footer for incompatible changes.

Examples:

```text
feat(lyrics): add word-level TTML timing
fix(playback): preserve position after source switch
test(lyrics): cover empty enhanced LRC spans
build(installer): align portable artifact names
docs: explain Spotify client configuration
feat(storage)!: migrate lyric cache keys
```

Commit bodies should explain why a non-obvious change is needed. Reference issues in footers, for example `Refs: #42` or `Closes: #42`.

## Enable local enforcement

From the repository root, run:

```powershell
./tools/setup-git-hooks.ps1
```

This configures this clone to use `.gitmessage` as its commit template and `.githooks/commit-msg` to validate new commit subjects. Merge commits, Git-generated reverts, and `fixup!`/`squash!` commits are accepted so normal Git workflows continue to work.
