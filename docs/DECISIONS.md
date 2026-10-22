# Decisions

This file records the decisions that shaped the first release, why they were made and what they cost.
It is meant to be updated when a decision is revisited, not rewritten to look inevitable.

## Stack: C# with Avalonia instead of Rust with Slint

The design document recommends Rust + Slint (option T1) and names C# + Avalonia as the fallback (T5).
The first version uses **.NET 10 + Avalonia 11.3.22**, because no Rust toolchain is installed on the
development machine and standing one up (rustup plus the MSVC linker for `windows` targets) would delay
a working build by hours, while Avalonia already provides first-class `TrayIcon`, theme variants,
compiled XAML and JSON-driven localization. Avalonia is MIT licensed, as is the rest of the stack.

Costs: cold start is roughly 1.0 s instead of the 300 ms target, and the published binary is ~70–90 MB
self-contained versus a few MB for Rust. The startup section below lists what was tried and what
remains. The core library has no Avalonia or Windows dependencies, so a different shell (or a Slint
front end) could reuse it.

Avalonia 12 was rejected because it was still a moving target with breaking changes; 11.3.22 is the
latest patch of the stable 11 line.

## Catalog sources: versions.yml first, HTML archive second, GitHub API not required

Measured on the development machine:

- `https://raw.githubusercontent.com/godotengine/godot-website/master/_data/versions.yml` returns the
  full catalog (about 45 KB, 365 releases at the time of writing) and is the primary source.
- The unauthenticated GitHub API (`api.github.com/repos/godotengine/godot-builds/releases/tags/...`)
  returned **403** with `core remaining: 0/60`, because the limit is per IP and the IP is shared. A
  launcher must work on a fresh install without a token, so the API cannot be on the critical path.
- `https://downloads.godotengine.org/?version=…&slug=…` redirects with `302` to object storage and
  supports `HEAD` and range requests, so a direct asset URL plus `HEAD` probing is enough to resolve and
  size downloads.
- The GitHub release download URL (`github.com/godotengine/godot-builds/releases/download/<tag>/<file>`)
  also redirects and serves identical bytes, making it a usable fallback when the official endpoint is
  down.

Consequences: asset digests are usually unknown, so verification falls back to the final byte count
(the download is reported as verified only when a digest exists). A user-configured mirror template is
supported for restricted networks. If optional GitHub tokens are added later, digests and release
metadata can be fetched without hitting the anonymous limit.

The `versions.yml` parser emits nested `releases:` entries as their own versions. The upstream file
nests `dev7`, `rc1`, `beta5` entries under a version, and those names are exactly the download ids; only
emitting the parent would hide most pre-releases.

## Asset resolution by ordered candidates, not a fixed table

The official slug names changed between branches: 3.6.3 publishes `x11.64.zip` and `osx.universal.zip`,
while 4.7.2 publishes `linux.x86_64.zip` and a single `macos.universal.zip` for both architectures (no
`macos.x86_64.zip` on that branch). A hardcoded version-to-slug table would rot immediately, so the
resolver generates ordered candidates per platform and edition (`mono_` prefixes included) and stops at
the first candidate that answers `HEAD` with success.

## Downloads: `.part` files, size checks, staging directories

- Downloads write to `<name>.part` and resume with a `Range` header, so a cancelled or interrupted
  transfer continues instead of restarting. A `416` with a matching size finishes the file; a mismatched
  size deletes it and starts over.
- Transient failures (408, 429, 5xx, socket errors) retry three times with 1/2/4 s backoff. A `404`
  moves on to the fallback URL instead of retrying.
- Archives are extracted into `staging/<id>-<edition>-<guid>` and moved into place only after the
  executable is found. A failed install therefore never leaves a half-populated version directory, and
  an existing install is never overwritten silently.
- Every write of `installed.json`, the catalog cache and `config.toml` goes through a temporary file and
  an atomic replace. Corrupt files are renamed with a timestamp and rebuilt rather than crashing the
  launcher.

## Settings: small hand-written TOML subset

The configuration is TOML so users can read and edit it, but the launcher only needs sections, string
keys, booleans and integers. A ~200 line parser covers that instead of adding another dependency; it
handles comments, quoted strings and literal strings, and rejects malformed lines loudly. If nested
tables or arrays are needed later, replacing it with Tomlyn is a contained change.

## One registry per version, edition and architecture

`installed.json` keys installations by `{version id}|{edition}|{architecture}`. That allows a Standard
and a .NET build of the same version to coexist in separate `editors/std` and `editors/dotnet` trees,
so the two downloads can never overwrite each other, supports registering an editor downloaded by hand
(never deleted by the launcher), and matches the folder layout `editors/std/<id>` and
`editors/dotnet/<id>`. The first installation is promoted to default automatically, and libraries without
a default are repaired on load by promoting the most recently launched editor.

## Single instance without extra dependencies

On Windows a named mutex plus an auto-reset event (`Local\GodotLauncher.SingleInstance`,
`Local\GodotLauncher.ShowWindow`) tells the running copy to show its window. On Linux and macOS there is
no cheap named event, so the first instance listens on a loopback TCP port derived from a hash of the
data directory and the second writes `show` to it. Both paths are a few dozen lines, need no IPC
package, and are covered by a manual test.

## Localization as data, not code

Every language is one JSON file in `Localization/`. Each file carries `Language.Code` and
`Language.Name`, so the settings list is built by enumerating the files; adding a language means adding
a file and nothing else. The XAML binds to typed groups (`Strings.Explore.Search`) generated from the
key structure, which keeps refactoring safe and missing keys visible.

Text that is derived from a status, rather than bound to a field, is stored as a small state and
rendered again whenever the language changes: the update status keeps its kind and version tag, and the
settings page re-raises its computed lines. Without that, a message rendered in the previous language
survives the switch — the bug that made "当前已是最新版本" stay Chinese in the English UI. Failure
details from the operating system stay in a tooltip instead of being appended to the localized sentence,
because Windows localizes exception text with its own UI language.

## Localization and view models are tested headlessly

`tests/GodotLauncher.App.Tests` runs the real view models on Avalonia's headless platform, which needs no
display and works on the CI runners. It asserts that both catalogs define the same keys and
placeholders, that every property exposed by `Strings` resolves, and that computed messages follow a
language switch. The same suite covers the proxy address rule and the project import flow, which are the
two places where recent bugs appeared. `dotnet test GodotLauncher.slnx` runs it together with the core
suite on every push.

## Startup: what was measured and what remains

Release build, self-contained single-file, ReadyToRun, on Windows 11 (three runs):

| Phase | Time |
| --- | --- |
| Services (settings, HTTP, caches) | ~40 ms |
| Cached catalog read | ~100 ms |
| Installation library read | ~133 ms |
| Window construction | ~454 ms |
| Platform window, theme and first render | ~290 ms |
| **First frame** | **873–939 ms** |

Comparison runs on the same machine: framework-dependent ReadyToRun 1065 ms, self-contained folder
ReadyToRun 1045 ms, and compressed single-file 1715 ms. The published build is therefore uncompressed
single-file ReadyToRun: it starts fastest and the zip/tar.gz archives still shrink it to about 50 MB.

Applied: the tray icon is created after the first frame (constructing `TrayIcon` alone cost ~580 ms);
secondary pages and Explore cards are created on demand; the catalog and library are read in parallel
with UI construction on worker threads. Tried without measurable gain: DirectComposition composition
modes, disabling extended client area, and single-file compression (much worse).

Remaining gap to the 300 ms target is dominated by Avalonia's platform initialization and theme
application. Options for a future release: publish with NativeAOT, trim Fluent theme resources, or move
the shell to a lighter toolkit while keeping the core library. This is documented as a known gap rather
than silently relaxing the target.

## Packaging and distribution

GitHub Actions publishes self-contained single-file ReadyToRun archives for `win-x64`, `linux-x64`,
`osx-x64` and `osx-arm64` (`build.yml` on every push, `release.yml` on `v*` tags with `SHA256SUMS.txt`
and generated release notes). Self-contained is the default because a launcher that requires a .NET
runtime install fails the "just works" requirement. The archives are not code signed yet; signing
certificates are a release-engineering task that needs accounts and secrets, so it is listed as a known
limitation instead of being faked.

## Known gaps

- Cold start ~0.9 s, target 300 ms (see above).
- SHA-256 verification only when a publisher digest is known; otherwise size-only verification.
- Launcher updates are explicit: the launcher checks GitHub in the background, but downloading and
  restarting is a user action. Fully silent self-updates are not attempted.
- macOS and Linux binaries are built by CI but were only run on Windows during development; tray
  behaviour and path handling on those platforms need a smoke test.

## Projects live in their own file

`projects.json` is separate from `installed.json`: a project references an editor, it is not part of the
installation record. Keeping them apart means uninstalling an editor never deletes project entries, and a
corrupt project list cannot break the editor library. Only the path, display name, detected version and
chosen editor are stored; everything else is re-read from `project.godot` when the list is shown or
refreshed.

## Editor selection is explicit, then version-based, then default

Opening a project first honours the editor the user picked for that row. Otherwise the launcher picks the
newest editor with the same major/minor version, preferring stable builds over previews. If the project
declares a version that is not installed, no editor is selected and the row shows a warning instead of
silently launching a different engine version. Only projects without a readable version fall back to the
default editor.

## Launcher updates replace files after exit

A running executable cannot overwrite itself on Windows, so an update is applied by a small script
(UpdateInstaller in the app project) that is started detached in a temporary folder. The script waits
for the launcher process id to disappear, unpacks the downloaded archive (Expand-Archive on Windows,
 tar -xf elsewhere), copies the files over the install directory, starts the launcher again and deletes
the temporary folder and the archive. The About section shows the check result, downloads the asset that
matches the host platform (win-x64, osx-arm64, ...), fills the button background while the label shows
the live speed, cancels on a second
click, and installs as soon as the archive is ready. The sidebar version row shows the same button so
an update can be started from any page. The install folder is probed for write access first: on a read-only location the launcher reports the problem instead
of leaving a half-replaced directory. Updates are opt-in; nothing is downloaded without the user asking.

## Proxy handling rebuilds HTTP clients

`HttpClient` instances cannot change their proxy after the first request, so changing the proxy mode
disposes the catalog and download clients and creates new ones. `SocketsHttpHandler` is configured
explicitly instead of relying on `HttpClient.DefaultProxy`, which keeps behaviour identical on Windows,
Linux and macOS and makes "direct connection" actually bypass a system proxy.

## Update checks use the latest release endpoint

The launcher compares its assembly version with the tag of `releases/latest`. A `404` is treated as "no
release published" rather than an error, which keeps forks and private repositories quiet. Assets are
picked by platform token; downloading an update copies the archive into the downloads folder and the
downloaded archive is applied by `UpdateInstaller` as soon as it is complete, so no manual replacement
step is left to the user.

## Rate limits fall back to the release page

The anonymous GitHub API allows 60 requests per hour per address, and this address is often exhausted.
A 403/429 from the API now triggers a second check against the public release page instead of failing:
`releases/latest` redirects to `releases/tag/vX.Y.Z`, the tag is read from the redirect, and asset URLs
are built from the known file names. The page costs one ordinary request and is not subject to the API
quota, so update checks keep working without a token.

## .NET archives wrap their payload in a folder

`Godot_v4.8-beta1_mono_win64.zip` contains a `Godot_v4.8-beta1_mono_win64/` directory, unlike the
standard archives which put the executable at the root. Instead of special-casing the .NET edition, the
executable finder searches the archive root and one nested level, and the installer unwraps a staging
directory that contains exactly one folder. Both editions therefore end up flattened in their install
directory, with `GodotSharp/` beside the managed executable.
