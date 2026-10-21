# Architecture

Godot Launcher is split into a UI-free core library and an Avalonia application shell. The core holds
everything that talks to the network, the disk or child processes; the shell only renders state and
forwards commands.

```
src/
  GodotLauncher.Core/        net10.0 class library, no UI dependencies
    Models/                  versions, releases, installations, download assets
    Catalog/                 versions.yml parser, fallbacks, cache, normalization
    Assets/                  per-platform download slug resolution
    Downloads/               resumable HTTP downloads with verification
    Install/                 zip extraction, installation and registry
    Launch/                  detached editor process launching
    Settings/                TOML model, parser and store
    Storage/                 paths, atomic writes, rolling file log
  GodotLauncher.App/         Avalonia 11 desktop application
    Services/                composition root, tray, single instance, autostart
    Downloads/               download queue and item view models
    Mvvm/                    base view models and dialog host
    ViewModels/              explore, installed, settings, shell
    Views/                   XAML views and code-behind
    Styles/                  theme resources and control styles
    Localization/            JSON catalogues and generated string groups
tests/
  GodotLauncher.Core.Tests/  xUnit tests for the core library
tools/
  generate_icons.py          regenerates the application icon
  capture_window.ps1         Win32 screenshot helper used during development
  click_window.ps1           Win32 click helper used during development
```

The core is referenced by the app, never the other way around. Tests exercise the core through the same
public API the app uses, and use a small in-process HTTP server (`TestHttpServer`) plus a stub
`HttpMessageHandler` instead of the public internet.

## Startup

`Program` parses `--background` and `--bench`, creates the data layout, opens the log, and takes the
single-instance lock. If another instance is running, it signals it to show its window and exits.
Otherwise Avalonia is started and `App` builds the object graph:

1. `AppServices` loads `config.toml`, builds the localization service, HTTP clients, catalog cache,
   catalog service, asset resolver, downloader, installer, installation library and download manager.
2. The cached catalog is read from disk on a worker thread and assigned before the window is created.
3. The installation library is loaded the same way.
4. `MainWindow` is created and shown. Explore cards, the tray icon and secondary pages are deferred to
   `DispatcherPriority.Background` work so the first frame is not blocked.
5. A background catalog refresh runs, and a 30 minute timer refreshes again when
   `refresh_interval_hours` allows it.

No network call happens before the first frame. Every operation that touches the network or a long file
runs on a worker thread; view models marshal results back with `Dispatcher.UIThread`.

## Catalog

`CatalogService` tries sources in order and stops at the first success:

1. `versions.yml` from the `godot-website` repository (primary, includes all channels and patch
   releases).
2. The HTML archive page at `godotengine.org/download/archive/` (emergency fallback, scraped with a
   generated regex).

Each source is conditional: the stored `ETag` is sent as `If-None-Match` for the source that produced
the cached catalog, and a `304` keeps the cached copies. A failed refresh never discards a good cache,
and a response that shrinks by more than half is rejected as likely truncated. Set differences produce
the ids shown with a `NEW` badge.

`VersionsYmlParser` emits one `GodotVersion` per top-level entry **and** one per nested `releases:`
entry, because the nested names (`dev7`, `rc1`, `beta5`, …) are the download ids. Normalization sorts
newest first by numeric version, stage (`dev < beta < rc < stable`) and stage number, deduplicates by id
and drops entries without a usable name.

## Downloads and installation

`AssetResolver` turns a version, edition and architecture into ordered candidate URLs: a user mirror
template first (if configured), then `downloads.godotengine.org` with the official platform slug, then
the matching `godot-builds` GitHub release asset. Candidate slugs include both current and legacy names
(`win64.exe.zip`, `windows_arm64.exe.zip`, `linux.x86_64.zip`, `x11.64.zip`, `macos.universal.zip`,
`osx.universal.zip`, …) and every candidate is probe-checked with `HEAD` before use.

The download pipeline is:

1. Resolve the asset URL.
2. Download to `<destination>.part`, resuming with a `Range` header when a partial file exists.
3. Retry transient failures (HTTP 408/429/5xx, IO errors) with backoff.
4. Verify the SHA-256 digest when one is known; otherwise verify the final size.
5. Extract into `staging/<id>-<edition>-<guid>`, rejecting absolute paths, traversal, symlinks and
   zip-slip entries.
6. Locate the editor executable, move the staging directory into place and register the installation.
7. Delete the archive and staging directory.

Leftover `.part` files are kept, so asking for the same download again after a restart resumes it.
Staging directories older than an hour are removed at startup instead of accumulating.

The registry (`installed.json`) records one entry per version, edition and architecture, including the
executable path relative to the install directory, size on disk, source, hash, pinned/alias flags, last
launch time and default flag. Writes are atomic (temporary file plus replace). A corrupt registry is
renamed to `installed.json.corrupt-<timestamp>` and rebuilt empty instead of blocking startup.

## Launching

`EditorLauncher` validates the executable, swaps to the `_console.exe` sibling when console output is
requested, prepends per-version arguments, and starts the process detached
(`DETACHED_PROCESS`/`CREATE_NEW_PROCESS_GROUP` on Windows, `setsid` on Unix) without waiting for exit.
The launcher then applies the configured after-launch behaviour: hide to tray (default), stay visible or
quit. `last_launched` is written asynchronously.

## Shell

- `MainWindowViewModel` owns navigation, the status/banner line, counts, the downloads drawer and the
  dialog host. Pages are view models created once; their views are created on first navigation. The
  banner only appears for release channels whose notification setting is enabled.
- `DownloadManager` runs at most `concurrent_downloads` jobs (1–4, default 2) from a FIFO queue. Items
  are deduplicated per version/edition/architecture and survive cancellation and retry.
- `TrayService` builds a native tray menu (show, launch default, recent five, refresh, settings, exit)
  and rebuilds it when the language, settings or installation list changes. If the platform has no tray,
  closing the window quits instead.
- `SingleInstance` uses a named mutex plus an auto-reset event on Windows and a loopback TCP listener on
  Unix; the second process signals "show" and exits.
- `AutostartService` writes the per-user autostart entry for the current platform when enabled.
- `LocalizationService` discovers `Localization/*.json` from the assembly. Each file carries
  `Language.Code` and `Language.Name`, and navigation strings come from groups such as `Explore.*`.
  `Strings` exposes typed groups so views never bind to raw keys. Changing the language swaps the whole
  `Strings` snapshot and raises change notifications.

## Theming

`Styles/Theme.axaml` defines semantic brushes twice, under `ThemeDictionaries` for `Light` and `Dark`.
Views only reference semantic resources, so switching `RequestedThemeVariant` restyles everything
without restarting. Channel accents (stable green, RC blue, beta orange, dev grey) are separate
resources used by the pill badges.

## Tests

`tests/GodotLauncher.Core.Tests` covers catalog parsing and fallback behaviour, normalization and
sorting, slug resolution, the downloader (resume, retry, fallback, checksum mismatch), zip extraction
and zip-slip rejection, executable discovery, installation and the registry, library persistence and
preview cleanup, TOML parsing and corrupt-file recovery, launching, and the rolling log. The suite runs
on Windows and Linux in CI.
