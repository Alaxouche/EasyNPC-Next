# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0.0] - 2026-08-18
### Added
- **Broken FaceGen detection.** NPCs whose chosen mod ships an empty or corrupt face mesh - which shows up as an invisible face in game - are now listed on the build's "All done" screen and in the log, so you can pick a different face or fix the source mod. EasyNPC can't rebuild a broken mesh, but it no longer ships one silently.
- **"Unused appearance mods" report (Maintenance page).** Lists installed face mods you never picked for any NPC, so you can safely remove the ones you only kept to compare faces.
- **Theme setting (System / Light / Dark)** in Settings, applied live and remembered between sessions.
- **The window remembers its size, position and maximized state** between sessions.
- **Batch "Set as Default Plugin".** A new Profile-page Batch action sets the selected mod as the Default (stats and behavior) plugin for every NPC in the current filter.
- **Online mugshot controls.** Online mugshots can now be turned off in Settings, there's a "Clear cache" button, each card links to the mod's Nexus page, and NPC Face Finder is credited.
- **Tooltips on the loading steps** (Mod Scan, Analysis, Profile) explaining what each one does.

### Changed
- **Online mugshots now work on every machine.** The requests no longer go through the Windows proxy resolver, which is missing on some (debloated / LTSC / N) Windows installs and made every online mugshot lookup fail there.
- **Faster mugshots.** Images are fetched and decoded in parallel, and the next few NPCs are prefetched in the background, so moving down the list is much snappier.
- **The Vanilla card shows the real vanilla face** again, instead of occasionally borrowing a random overhaul's face.
- **Mods that don't change the face** (USSEP, AI mods, merges...) now show the vanilla face instead of a blank silhouette.
- **Auto-assign lists which NPCs would change** before you confirm, not just the count.
- **Cards for plugins that aren't loaded are hidden** from the mugshot gallery.
- **Smaller, cleaner download.** The build ships as a single self-contained exe instead of a folder full of DLLs.

### Removed
- **Experimental in-app 3D face preview and mugshot generation.** Online mugshots (NPC Face Finder) cover the same need, so the early, imperfect renderer and its Settings toggle were removed.

## [1.0.0.0] - 2026-08-18
### Added
- **Child NPC support (EXPERIMENTAL, off by default).** Child NPCs are normally skipped, because merging their appearance can give them an adult body if the race/skin isn't handled exactly right. A new Settings option ("Include child NPCs") lets child overhauls (RS Children, The Kids Are Alright, etc.) be merged. For children the merge keeps the child's race and child skin and pulls the child race into the output (so the child mod doesn't become a master), which should keep them children. Because a wrong result here is very visible, this ships off by default and must be verified in game (spawn a few children and check their bodies) before trusting a full build.
- **Online mugshots from NPC Face Finder (on by default).** Installed face cards that have no local mugshot are filled in with an image from the open [NPC Face Finder](https://npcfacefinder.com) API. It runs asynchronously, so it never slows the gallery (the local packs render instantly and the online images arrive a moment later), and it only touches cards for mods you actually have - it never adds cards for mods you don't. Faces are matched to your installed mods by Nexus mod id first (so a differently-named mod folder still matches) and mod name as a fallback. Images are cached on disk (converted from WebP to PNG) so repeat views are instant. Offline or on any failure it just does nothing, and the local packs and 3D preview still work. Every face credits its mod author - please support them.
  - Matching also falls back to the plugin file name (e.g. "TSOSRefined.esp" -> "True Sons of Skyrim Refined"), which survives "compilation" installs where several mods share one mod-manager folder and the individual mod's name and Nexus id are lost. Cards that still can't be matched fall back to the 3D preview.
- **Split output into multiple plugins (EXPERIMENTAL, off by default).** A new build option ("max NPCs per plugin") splits the merge into several plugins so no single one exceeds that count, to dodge "Too Many Masters" errors. Each split carries its own copy of the records it references, so the plugins don't depend on one another - enable all of them in your load order. Loose-file builds only for now (turn off archiving). Set to 0 for a single plugin.
- **Forward custom races (EXPERIMENTAL, off by default).** A new build option keeps an NPC's custom race from the face plugin (e.g. Project ja-Kha'jay, custom child races) instead of falling back to the default race, which is why those NPCs previously looked wrong. The race and everything it references are duplicated into the merge (via Mutagen's dependency merge-in), so the race mod does not become a master. This is heavy record surgery - verify the affected NPCs in game and watch the master count on very custom-race-heavy setups.
- **Option to keep FaceGen loose when packing into an archive.** A new sub-option under "Pack files into archives" ("Keep FaceGen as loose files") packs everything into the BSA except the FaceGen (facegeom meshes and facetint textures), which stays loose. Loose FaceGen always wins over archived copies from other mods, so this avoids head/neck seams and stale faces when several mods ship FaceGen for the same NPCs. Off by default.
- **Standalone post-build verifier (`EasyNPC-Verify.exe`).** The post-build verification is now its own separate app you can launch any time to check the merged output's status and conflicts, instead of only right after a build. It reuses EasyNPC's game/mod setup, so launch it the same way (from your mod manager). It has its own icon (the masks with a green check).
  - Restyled to match EasyNPC Next: an accent header with the masks-and-check emblem and version badge, a summary row (NPCs verified, FaceGen conflicts, tint mismatches, coloured by result), and a card layout.
  - A **Re-run** button re-checks without relaunching. It reuses the already-loaded game data (only the confirmation step is one-time), so a re-run is much quicker than starting the app again.

### Changed
- **Faster texture-path extraction.** Reading texture references out of the meshes serialized all NIF parsing behind a single lock, so only one CPU core did the work. Parsing now runs in parallel across cores (each mesh on its own thread, the same way the FaceGen edits already ran), which is a big speedup on large merges.
- **Live item counts on the build steps.** Each step's progress bar now shows a running "44 / 814" count next to it, so you can see how far along a step is, not just that it's busy.
- **Much faster builds when the output is on the same drive as the mods.** Copied textures and resources are never edited after the fact, so instead of copying the bytes the build now hard-links them into the output when the source is on the same volume - the file appears instantly and takes no extra disk space (the same trick mod managers use for deployment). Cross-drive sources, and anything read out of a BSA, still copy normally. For the biggest speedup, put the EasyNPC output mod on the same drive as your mod files.

### Fixed
- **BSA packing produced oversized (>2 GB) archives that can crash the game.** The archive splitter tracked the running compressed-size estimate in a 32-bit integer, which overflowed past ~2 GB and silently disabled the size check, so textures kept piling into one archive up to a 15 GB uncompressed cap. Archives are now split so no single one holds more than 2 GB of uncompressed data, which guarantees the packed file stays safely under the 2 GB limit whatever the real compression ratio is (the same conservative, size-based split that tools like BSArch use), and the overflow is fixed. Archiving is on by default again.
- **Invisible faces on generic NPCs (guards, hunters, bandits, especially female) whose overhaul ships no FaceGen.** The template-head fix cleared the "Traits" inheritance flag whenever the chosen face was the NPC's own. But some overhauls give an NPC head parts without shipping a per-NPC FaceGen, and clearing the flag there left the NPC with head parts but no FaceGen, so the face rendered invisible. The flag is now cleared only when a FaceGen is actually available for that NPC; otherwise the NPC keeps inheriting the template's FaceGen and stays visible.
- **Invisible/black heads on template-based NPCs (many guards) after an overhaul.** NPCs like guards inherit their appearance ("Use Traits") from a template. When an overhaul (e.g. True Sons of Skyrim, Men of Skyrim) gives such an NPC its own face, the merge used to decide "inherits traits, so no FaceGen and keep the template link" from the *default* plugin instead of the *face* plugin. The result was an NPC with a custom head but no FaceGen and the Traits flag still set, so the game loaded the template's FaceGen and the head failed to render. The merge now bases these decisions on the face plugin: such NPCs get their own FaceGen, and the "Traits" template flag is cleared (with race/voice backfilled) so the merged face is actually used.
- **Hundreds of bogus "missing asset" entries in the build report.** Texture paths were extracted from meshes by scanning the raw NIF bytes for ".dds" strings, which glued adjacent binary strings together into paths that never existed and reported them as missing. Meshes are now parsed properly with NiflySharp, reading only their real texture-set paths.
- **FaceGen textures recovered from mangled mesh paths.** Some mods ship FaceGen meshes whose texture paths are broken (a missing folder separator like "brows\brow.dds" written as "browsbrow.dds", or a duplicated segment like "Head\Head\"). The game can't find those either, so those NPCs are missing textures in the base mod. When a texture isn't found, the build now tries a few de-mangled variants and, if the real file exists, copies it to the path the mesh expects - so the merge fixes textures that were broken in the source mods.
- **The spinning build indicator now animates correctly.** The three-cog glyph rotated as a whole around a central point; it's now two cogs that each spin on their own centre, in opposite directions and at different speeds.
- **Post-build verification no longer flags a loose-file build as broken.** With BSA packing off (the default), the merge has no archives, which is expected; the verifier used to report "some archives missing / not readable" and show "Problems detected". It now recognizes a loose build, skips the archive/dummy-plugin checks, and shows a clear "Loose file build (no BSAs)" line instead.
- **Archived textures and FaceGen (NIFs) could be reported missing / not copied into the build.** The archive list was enumerated from `env.DataFolderPath`, but the file providers read archives from the *real* data directory (which may be a `data` subfolder of that path). When the two differed, no BSAs were found, so every archived texture and mesh was treated as missing and left out of the merge. Archives are now enumerated from the exact same folder the providers read from, and any archives whose names don't follow the `<plugin>.bsa` convention are included too, so their contents are found instead of being reported missing.

### Changed
- **3D face rendering no longer runs on the UI thread.** Parsing NIFs and decoding large DDS textures now happen on a dedicated background render thread, so generating mugshots for a whole load order (or just showing a single preview) doesn't freeze or stutter the interface. Rendered previews are also cached, so revisiting an NPC is instant instead of re-decoding its textures. (Experimental feature, still off by default.)

### Added
- **Built-in Racial Skin Variance (RSV) exclusions.** A new build output option ("Generate Racial Skin Variance (RSV) exclusion file", off by default) writes a SPID `..._DISTR.ini` into the merged mod that tags every NPC given a modded face with the `RSVIgnore` keyword. This stops RSV from repainting its racial skin over the face you chose in EasyNPC - the well-known "1.5-2 faces" / dark-face conflict - and it stays automatically in sync with your profile, replacing the separate "EasyNPC RSV Excluder" tool. Harmless if RSV isn't installed.
- **Experimental 3D face preview is now an opt-in setting, off by default.** All of the in-app face rendering (the "3D Preview" button, live faces on the mugshot cards, and the Maintenance-page mugshot generator) is now gated behind a new **Experimental** section in Settings ("Enable in-app 3D face preview and mugshot generation"). It ships **disabled**; enabling it takes effect immediately. This keeps an early, imperfect feature out of the way unless you deliberately turn it on.

### Fixed
- **"Apply face ... to all filtered NPCs" (Batch menu) could not be clicked.** The menu item's enabled state didn't refresh when you selected a mugshot, so it stayed greyed out. Selecting a face now enables it correctly (and the item's label updates to the chosen mod).

### Added
- **Auto-assign recommended faces (batch).** The Profile page's "Batch" menu has a new "Auto-assign recommended faces to all filtered NPCs" action: in one pass, every NPC in the current filter gets the face EasyNPC would recommend (the last plugin that modifies that NPC's face, resolved to its source) - so you only tweak the exceptions instead of clicking through thousands of NPCs. It reuses the same recommendation engine as "Reset Face Selections", scoped to the face plugin only (the Default/behavior plugin is left untouched), shows how many NPCs would actually change before applying, and reports the result.
- **In-app 3D face preview (flagship, first version).** The Profile page now has a working "3D Preview" button that renders the selected NPC's head from the **actual installed FaceGen mesh and textures** - no external mugshot pack required. It resolves the FaceGen NIF and its DDS textures through the app's existing file provider (loose files *and* BSAs), parses the geometry with NiflySharp, decodes textures with Pfim, and rasterizes the head with WPF 3D. The head skin uses the NPC's **per-NPC facetint** (skin tone, warpaint and complexion), which also means the face still renders when the base skin texture only exists inside a vanilla BSA. The camera frames on the head shape itself, so every preview uses a **consistent head-and-shoulders zoom and a straight-on angle** regardless of hair length, and stray/mis-placed geometry can no longer shrink the head to a dot. Eyebrow/eyelash overlay meshes (which rely on alpha transparency that basic WPF 3D renders as an opaque black band) are skipped in favour of the brows baked into the facetint. This first version is deliberately basic (flat diffuse per mesh part; no specular/subsurface yet - a faithful, recognizable preview rather than a game-engine screenshot) and renders a still image.
- **Live face on the mugshot cards.** When the selected NPC's face has no packaged mugshot image, the card at the bottom of the Profile page now shows the face **rendered live** from the installed FaceGen (via the same renderer) instead of a grey silhouette - so you get a real preview inline even without mugshot packs. Only the selected-face card is filled in (the renderer sees the installed/winning FaceGen, which represents that face); other cards keep their silhouettes, and cards that do have a packaged mugshot are unchanged. Rendering happens once per selection and only when a silhouette would otherwise show.
- **Offline mugshot generation.** The Maintenance page has a new "Face previews (mugshots)" section that renders a mugshot for every NPC from your installed FaceGen and writes it into your mugshots folder, in the exact layout the app reads (`<mugshots>\<face mod>\<base plugin>\00<formid>.png`) - so it can **fill in or replace external mugshot packs** for whatever mods you actually have. It shows a live count, can be cancelled, skips NPCs that already have a mugshot (unless you opt to overwrite) and those without an installed FaceGen, and refreshes the mugshot index when done so results appear without a restart. Built on the same renderer as the 3D preview; rendering is one-at-a-time, so a large load order takes a while.

### Changed
- **Renamed to "EasyNPC Next"** to mark this reworked fork. The display name (wordmark, window title "EasyNPC Next - v1.0.0.0", startup dialogs, exe product metadata) is updated, while the executable itself stays `EasyNPC.exe` so mod-manager registrations, the application icon and existing paths keep working unchanged.
- Interface, navigation and wording pass:
  - **Consistent terminology.** The two core concepts are now called "Default Plugin" and "Face Plugin" everywhere. The Profile page previously called them "Default Source" / "Face Source" in its tooltips and "Set as default" / "Set as face" on its buttons, while the Build screen and Maintenance page already said "Default/Face Plugin" - three names for the same thing. They're unified on the wording the community and wiki use.
  - **Guided empty state.** When no NPC is selected, the Profile page now shows a short hint ("Select an NPC ... Pick an NPC from the list on the left to see which plugins provide it and choose the face you want.") instead of blank panels.
  - **Log moved to the navigation footer.** Log is a diagnostic surface most users never need, so it now sits apart (in the pane footer) from the main Profile -> Build workflow instead of being a top-level tab of equal weight.
  - **Clearer labels.** The Profile page's right-hand panel is now titled "Source plugins" instead of "Provided/Overridden In".
  - **Page subtitles.** Each section header now has a one-line description under its title (e.g. Profile -> "Choose each NPC's appearance", Build -> "Review your choices and generate the merged plugin"), so what each screen does is obvious at a glance.

### Fixed
- Character-generation face presets (e.g. the vanilla RaceMenu presets that mods like Better Argonian Horns edit) are no longer treated as NPCs. They're actor records used only by the character creation menu and never have FaceGen files, so EasyNPC was raising bogus "makes edits to <preset> that require a FaceGen file, but it was not found" alerts for every mod that touches them. NPCs flagged "Is CharGen Face Preset" are now excluded from the profile and build entirely, the same way the player, children and audio-template NPCs are - fixing the false alerts globally for any such mod.
- The application/exe/window icon is now the amber theater-masks emblem used in the app's wordmark, replacing the old icon.
- The permanent build action bar at the bottom of the build screen now uses a neutral surface colour that matches the rest of the app (with the amber accent divider) instead of the off-theme navy panel.
- NPC face-selection cards render more cleanly: the high-resolution silhouette placeholder is now scaled with high-quality filtering (no more jagged outline), the card is sized so the mod/plugin name below it is fully readable, and the mugshot row is a little taller.

### Changed
- Version set to 1.0.0.0.
- BSA archiving is now **disabled by default** in the build output options (loose files only).
- The texture path extraction timeout now **defaults to 0 (disabled)**. Files are never skipped for time; builds are as complete as possible out of the box. (Still adjustable in the Output options if you want a per-file cap.)

### Fixed
- NPC overhauls you deliberately use as a face source were wrongly flagged as "suspicious masters". The warning is meant for overhauls that sneak in as required masters, not for the overhaul you chose. A plugin is now only flagged as suspicious when (a) at least one NPC actually uses it as its Default Plugin - so double-clicking it shows the affected NPCs, as the help text promises - and (b) you are not already using it as a face source. Example: True Sons of Skyrim, used as the face source for hundreds of NPCs and only incidentally the default for a few, is no longer flagged. Such plugins still appear in the master list for review, just without the suspicious highlight.
- No NPCs detected after the Mutagen upgrade (analysis finished instantly with 0 NPCs). The load order was being read through `PluginListings.LoadOrderListings`, which only returns a subset of the load order; the correct replacement for the old `LoadOrder.GetListings` is `LoadOrder.GetLoadOrderListings`, which returns the full combined order (implicit masters + Creation Club + plugins.txt). With the subset, every plugin depending on a non-base master was treated as unloadable and skipped.
- Crash while loading plugins after the Mutagen upgrade. Mutagen 0.53's archive-path helpers sort archives with a comparer that throws `NotImplementedException` for ordinary plugin BSAs, which crashed as soon as the file provider was built with a real modlist. Archive order is now assembled entirely by hand from the data folder - the base-game archives listed in the ini, then each plugin's `<name>.bsa` and `<name> - Textures.bsa` that exist, in load order - without calling Mutagen's archive sorting at all.

### Changed
- Upgraded Mutagen from 0.31 to 0.53.1. This is the plugin-reading library; the old version predated the Creation Kit 1.6 record formats, and the upgrade should let modern overhauls (and their newer plugin headers) be read without the parse errors seen on recent mods.
- Migrated from .NET 5 (end-of-life) to .NET 8 LTS, along with Fody, PropertyChanged.Fody, ModernWpfUI and System.IO.Abstractions updates. This was the prerequisite for the Mutagen upgrade.
- Version set to 0.7.0.0, shown top-left both in the window title ("Easy NPC - v0.7.0.0") and as an amber version badge next to the app wordmark in the navigation bar.
- Interface refresh to mark the reworked build: an app wordmark (masks icon + "Easy NPC" + version badge) anchors the top-left; section headers get an accent bar; a cohesive amber accent (light and dark themes) unifies buttons, selection highlights and the navigation indicator; the top navigation shows an icon next to each section; content cards have rounded corners and a soft shadow for depth.
- NPC face-selection cards (mugshots) are now framed as proper cards: a rounded border with a background so faces and the silhouette placeholder read clearly instead of floating on black, a "Vanilla" tag on the base-game card, a rounded accent check badge on the selected card, and warning icons next to status messages ("Mod not installed", "Plugin not loaded", etc.).
- The build screen's **Build button is now pinned to the bottom of the window** as a permanent action bar (with an accent divider and a larger button), so it's always reachable without scrolling past the details sections. The bar still shows the current status ("Ready to build.", or why building is blocked).
- Updated MessagePack (Vortex manifest reading) from 2.2.85 to 2.5.302, resolving all known security advisories; this was blocked on .NET 5 and is verified working on .NET 8.
- Visual polish: hovered and selected mugshots are now clearly outlined (accent color), group panels have rounded corners, the NPC list uses subtle alternating row shading, the profile layout adapts to wide screens instead of using a fixed-width list, and the window has a sensible minimum size.

### Added
- The build progress screen now shows live metadata: overall progress percentage and bar, elapsed time, tasks completed / total, and an estimated time remaining. Progress and the estimate are weighted by task cost (file-copy and texture tasks count for much more than trivial ones) and the remaining-time figure is smoothed, so it no longer jumps around at task boundaries.
- Each build task's duration is now written to the log, to make it easy to see which phase dominates a build.
- **Batch face assignment.** The profile toolbar has a new "Batch" menu: apply the currently-selected face (mod) to every NPC in the current filter at once, or reset every filtered NPC to vanilla. Combine it with the filters (e.g. "Provided in: <mod>") to retexture whole groups of NPCs in one click instead of one at a time. Each action confirms first and reports how many NPCs changed.
- **Non-destructive load-order sync.** "Reset NPC Defaults" and "Reset Face Selections" on the Maintenance page now show a preview first - how many NPCs would change, with concrete before/after examples - and only apply once you confirm.
- Quick access to logs: the Log screen now has "Open log file" and "Open log folder" buttons.
- Unit test coverage for the record import pipeline (`RecordImporter` race handling, player record exclusion, profile log corruption tolerance).
- Old log files are now cleaned up automatically at startup, keeping the 10 most recent sessions.
- The build completion screen now shows how many NPCs were merged, and lists any NPCs that had to be skipped due to record errors - including which plugin and why - with a link to the full log file. Skipped NPCs are also written to `build_info.json`.

### Performance
- File and FaceGen copying now scale their worker count with the CPU (`Environment.ProcessorCount / 2`, clamped to 4-8) instead of a fixed 4. On modern multi-core machines with SSD/NVMe storage this speeds up the copy-heavy parts of the build; the lower bound preserves the previous behavior on low-core machines and the upper bound avoids I/O thrashing (which would matter on a mechanical HDD). The set of files copied is unchanged, so build output is identical. (Texture path extraction already used all cores.)
- Loose files (the bulk of a texture-heavy build - real timings show texture copying is ~80% of total build time) are now copied with a native OS copy instead of being read into a managed byte array and written back. This removes one large-object-heap allocation per texture (many are 10-80 MB) and lets the kernel use its optimized copy path. Archived files, which have no on-disk path, still use the read-into-memory route. Copy output is byte-for-byte identical. Note: the build is fundamentally disk-I/O bound (~96% of the time is copying textures and FaceGen data), so the single biggest lever remains fast storage (NVMe) for both the mod sources and the output folder.
- The build progress bar and "estimated time remaining" are now weighted by the *measured* relative cost of each pipeline task (texture copying dominates, then FaceGen copying, then everything else) instead of a rough guess, so the bar tracks real progress and the ETA counts down steadily instead of swinging at task boundaries. Per-task durations are also logged, so the dominant phase of any build can be identified from the log.

### Fixed
- #138: The player character record is now excluded from profiles and builds. Patching it (in particular writing a Worn Armor to it) broke custom and beast-race player characters in game.
- #212: A corrupted or truncated line in the profile autosave log no longer crashes the app on startup or maintenance; the unreadable event is skipped and the rest of the profile is preserved.
- A single broken or unreadable NPC record no longer fails the entire build ("Something Went Wrong" during Import NPC Defaults / Apply Visual Attributes, as in #219/#221/#227). The affected NPC is excluded from the merge, keeps its regular load order behavior, and the error is logged with details.
- Skins (WNAM) copied from overhauls are now forwarded faithfully. Cloned Armor Addons keep their original race assignments whenever those races come from the merge's masters, instead of all being rewritten to `DefaultRace`; this prevents same-slot addons from competing as wildcards, which could give NPCs the wrong body (e.g. human bodies on Khajiit with Project ja-Kha'jay) or invisible bodies (e.g. with Ordinary People).
- NPC-specific race patching of Worn Armors now uses the addon's *original* race list, recorded before import rewrites it. Overhauls that assign custom races (such as Project ja-Kha'jay's furstock races) now get the NPC's actual race attached to the correct body addons.
- The base game plugins (Skyrim.esm, Update.esm and DLCs) are always treated as masters of the merged plugin, since NPC overrides depend on them anyway. Vanilla armor addons, footstep sets and races referenced by overhauls are passed through instead of being cloned and stripped.
- Valid Races are now patched on cloned head parts' Extra Parts as well, not just on the head parts directly assigned to the NPC.
- Beast transformation addons taken from modded transformation races are imported into the merge instead of referenced directly, avoiding unexpected master dependencies.

## [0.9.6] - 2022-11-10
### Added
- #80: New "Provided in" filter in profile view, shows all NPCs which *can* be affected by a given mod, even if they are currently pointing to different mods.
- #139: Able to scroll long mugshot rows using shift + mouse wheel.
- Output mod directory now contains a `build_info.json` which includes a list of any files that failed to copy or analyze, for help with troubleshooting missing meshes/textures and other in-game issues.

### Fixed
- #131: App no longer crashes silently when there are no build alerts to show.
- #133: Beast transformations (werewolf, vampire lord, etc.) should now work with all modded NPCs.
  - All Worn Armors, whether vanilla or modded, are patched in order to match the specific NPC's race and automatically include beast addons, even if the original mod left them out.
- #135: Eliminate `WindowChrome` related crashes associated Logitech SetPoint software.
- #164: All build tasks honor dewiggifier setting; blackface will no longer occur with mods such as High Poly NPC Overhaul 2.0 when wig conversion is disabled.
- #166: Avoid a stack overflow crash when launched with mods containing circular references.

### Changed
- #164: Wig conversion is now disabled by default, since Worn Armors (including wigs) are fully supported.
  - There is not much of an advantage to dewiggifying anymore; it makes the build take longer, and creates a minor risk of in-game issues.
- #165: Throttle texture path extraction and add a per-file timeout.
  - This should help many (not necessarily all) users who are experiencing long wait times during the "Extract Texture Paths" part of the build, and make the progress reporting more accurate for all users.
  - Timeout can be configured in the Output section of the build screen. Default, and recommended, is 30 seconds per file. Any files that time out will be reported in the new `build_info.json` for potential follow-up.

## [0.9.5] - 2021-10-09
### Added
- Brand-new build screen. Details on the [wiki page](https://github.com/focustense/easymod/wiki/EasyNPC-%E2%80%90-Build). Main features:
  - Single-click builds - no more having to click through multiple screens.
  - Lots more statistics about the pending build, and some predictive info such as file sizes.
  - New "NPCs" report showing both the NPCs that are _and aren't_ included in the build.
  - Improved UI for Master Dependencies, easier to read and includes a category for each.
  - Missing-assets check to warn about files that can't be found and won't be copied.
  - All checks done in real-time - build stats automatically update as settings and profile are changed.
  - Option to disable BSA creation (i.e. loose files only).

### Changed
- #124: Ensure NPC head parts are flagged as non-playable so that they don't crash the race menu in game.
- #127: Show a useful error when EasyNPC is started with invalid command-line options.
- #121: [Vortex Extension] Obtain correct game data path from Vortex.

### Fixed
- #95: Injected records from mods such as Interesting NPCs Visual Overhaul are now merged properly.
- #118: Fixed single-template detection logic that was accidentally excluding some NPCs from the profile.
- #119: Patch the Valid Races form lists copied from overhauls to prevent unexpected master dependencies.
- #122: Double-clicking on vanilla mugshot now updates the face selection.
- #123: Read file priorities from Vortex so that Post-Build Report shows correct status.
- #126: Skip and report broken plugins instead of "crashing" on startup.
- #128: [Vortex Extension] Use correct substitution for `USERDATA` token.

## [0.9.4] - 2021-09-21
### Added
- **Post-Build Report**: A major new feature/app mode designed to be run on the _final_ mod order, just before launching the game. Features include:
  - Checks for the integrity of the EasyNPC mod itself, including merge plugin, dummy plugins and archives.
  - Facegen/facetint consistency checks on all NPCs managed by EasyNPC. (NPCs _not_ customized by EasyNPC are ignored.)
  - An automated workaround to **extract conflicting files** from the EasyNPC archives in order to resolve loose-file conflicts.
    - This workaround is primarily intended for scenarios where the conflicts cannot be eliminated by disabling the conflicting mod - especially mods with a wide scope, such as EEO or BUVARP, or some of the conflict-resolution patches included in popular mod guides and Wabbajack lists.
  - To run the Post-Build Report, add the `-z` or `--post-build` command-line option to your normal options.
    - Vortex users will see a new "EasyNPC Post-Build" action next to the EasyNPC Launcher after upgrading their extension.
    - Mod Organizer users should configure this as a new executable for convenience.
    - Make sure to run this mode on your _final game profile_ - i.e. the one with the EasyNPC mod active and other overhauls disabled, _not_ the profile you'd normally use to run EasyNPC to build a new merge.

### Changed
- [Vortex Extension] Game ID is obtained from the current profile, instead of being hardcoded to `skyrimse`. Note that it is still required to set the `-g` or `--game` command-line option in order to use EasyNPC with a game other than Skyrim Special Edition - this change only ensures that the correct Vortex mod list and staging directory are used.
- [Vortex Extension] Don't show EasyNPC actions in the mod toolbar for unsupported games.

### Fixed
- Multiple marks/scars and other additional/extra head parts are now correctly carried over in the merged plugin. Previously, only one was being copied, which may have resulted in a few rare blackface issues.
- Fixed reversed archive ordering in `GameFileProvider`, which caused the lowest-priority mod to be used for shared assets instead of the highest-priority mod. Facegen data was never affected, only shared textures, morphs, etc.
- [Vortex Extension] Additional command-line parameters configured in the dashboard are now passed to the app.
- [Vortex Extension] Correctly handle path substitutions like `{userdata}` and `{game}` in the staging directory path.

## [0.9.3] - 2021-09-06
### Added
- #107: Essential NPC references are now checked on startup, and errors reported. Prevents late-manifesting crashes during build due to being unable to import some dependencies.

### Changed
- #102: Don't check for named instances of Mod Organizer when started under a "locked portable" instance (e.g. Wabbajack builds).
- #114: Obtain game path from Mod Organizer configuration. Makes EasyNPC compatible with Serenity 2 and any other instances with a "stock game" copy. Frequently eliminates the need to configure a `-p`/`--game-path` option.
- Prioritize the mod manager's mod directory by default, only using the manually-configured directory as a fallback. Can be disabled in the app settings to restore old behavior of always using the same mod directory.
- Build pipeline now logs every item per task in debug mode, to help with identifying obscure mod-specific errors.

### Fixed
- #100: Updated icon and highlight colors under Windows dark theme to improve visibility/legibility.
- #103: Synchronize profile writes to prevent corruption when importing a saved profile.
- #105: Ensure output directory exists when saving patch, in case it wasn't created by previous steps.
- #113: Fix plugin customizations being ignored due to premature activation of Mutagen environment.
- Sort master references when saving merge plugin. Fixes the semi-infamous "Finding Helgi" infinite-load bug.

## [0.9.2] - 2021-08-27
### Added
- Always show NPCs with available FaceGen Overrides, even if they have no override records.

### Fixed
- #101: Fix the FaceGen Override system in general - almost no part of it worked correctly.
  Standalone (no-plugin) overrides can now be double-clicked properly, overrides will be restored when relaunching the app,
  will not be overwritten by implicit (non-user-initiated) plugin syncs, checkboxes actually show on the correct tile, etc.
- Don't include mugshots referencing archives in disabled mods.
- Fix missing-plugin reset becoming broken again due to the loading process selecting an alternative plugin and the reset not actually
  changing it away from that value - however, it still needs to write to the profile and clear the "missing" flag.

## [0.9.1] - 2021-08-22
### Added
- Hide disabled Vortex mods from mugshots/profile - for parity with Mod Organizer. (Requires Vortex extension 0.1.4)

### Removed
- Remove "Only Face Overrides" filter from the UI since it no longer does anything. NPCs without face overrides are excluded at load.
- Remove most dewiggifier warnings. These now fall back to Worn Armor import and are not important. A lower-priority, generic
  informational message is produced instead.

### Changed
- Audio Template NPCs are filtered from the profile.
- Make imported Worn Armor/Addon races match actual usage by NPCs instead of trying to generate the list from playable races.
  Solves a few issues related to missing/invisible bodies for less-common overhaul races such as Elder.

### Fixed
- #98: Default mugshot paths (i.e. inside EasyNPC directory) no longer break mugshots.
- #99: Fix duplicate mods showing up in mugshots when running under Mod Organizer due to missing or deleted download metadata.
- Fix build failures due to "hard null references" in record data - references set to zero value instead of being unset.
- Fix game path (`-p` or ``--game-path`) parameter which was not working correctly.

## [0.9.0] - 2021-08-19
### Added
- #7: Import custom bodies (Worn Armors) from overhauls. Includes wigs that cannot be de-wiggified.
- #32: Preliminary support for Skyrim VR, and possibly other editions, through the `-g` or `--game` parameter.
  Also supports custom game paths with `-p` or `--game-path` parameter.
- #90: Recognize templated NPCs and handle accordingly. Depending on the scenario, this may block changes and display a warning in the UI,
  or exclude them from the profile entirely.

### Removed
- #64: No longer show warnings when NPC race is changed. This is unnecessary with full head-part checks.

### Changed
- #10: Read mod and profile metadata from Mod Organizer. Disabled mods will no longer show up, and mugshot synonyms are required less often.
- #49: Most patches for NPC overhauls are detected, and will not be chosen at first load or on profile reset.
- #64: Override all head parts in merged patch, which supports many if not most changes to NPC race or sex.
- #74: Faster builds with parallel file copying and parallel tasks in general.
- #76: Use entire mod list for locating dependent resources. Eliminates most in-game problems that were due to missing assets.

### Fixed
- #42: Make colors referenced by Head Parts standalone. Fixes another rare "unexpected masters" issue.
- #75: Properly reset filters when jumping to a master dependency from the build screen.
- #77: Fix scrolling in pre-build screens.

## [0.8.8] - 2021-07-24
### Added
- #17: Prominently display master dependencies before build, and provide a quick path to fixing them.

### Changed
- #71: Automatically exclude NPC races that don't use the facegen system (most non-human NPCs).

### Fixed
- #63: Fix the previously-nonfunctional corrupt BSA detection.
- #67, #68: Significantly improved handling of bad form ID references when loading, none should crash the loader anymore.

## [0.8.7] - 2021-07-22
### Changed
- #40: Add warning text and coloring around "trim" operation due to potential of profile corruption.
- #56: Pre-test plugins and mark as unreadable in loader instead of allowing them to crash the app on startup.
- #60: Exclude child NPCs due to severe incompatibility with child mods such as RS Children and The Kids Are Alright.
- #62: Show clearer warning when game data cannot be found.
- #64: Show build warnings when an overhaul changes an NPC's race.

### Fixed
- #63: Unreadable BSAs will emit a build warning instead of crashing the app.
- #65: Prevent profile corruption when a new mod is added that overhauls previously-ignored NPCs.
- #66: Fix some profile entries pointing to missing records failing to reset.

## [0.8.6] - 2021-07-18
### Fixed
- #36: Ignore non-race entries in Valid Races form list.
- #46: Fix another case-sensitivity bug for files in BSAs leading to build failures.
- #47: Use default when face tint record has null interpolation value.

## [0.8.5] - 2021-07-18
### Fixed
- #43: Handle additional texture path scenarios that were leading to missing textures.

## [0.8.4] - 2021-07-18
### Fixed
- #41: Use case-insensitive name comparisons in build checks (fix spurious "mismatch" warnings).
- #42: Ensure that Hair Color and Alternate Textures are made standalone.
- #43: Fix missing textures due to absolute/rooted texture paths in FaceGen files.

## [0.8.3] - 2021-07-17
### Fixed
- #34: Fix crash on startup due to missing mod names in Vortex manifest.
- #35: Fix crash on build due to case-sensitive mod name comparisons.

## [0.8.2] - 2021-07-17
### Fixed
- #31: Fix crash on build due to missing `BuildReportPath` default (MO2 only).

## [0.8.1] - 2021-07-17
### Fixed
- #28: Fix crash on load due to case-sensitive comparisons of archive and plugin names.
- #29: Support launching from Mod Organizer named instances.
- #30: Fix crash on startup due to JSON parse error from non-numeric Vortex mod IDs.

## [0.8.0] - 2021-07-11
### Added
- Support for Vortex Mod Manager.

### Changed
- Mod directory is auto-detected on first start.

### Fixed
- External hyperlinks now open the browser as expected.

## [0.3.0] - 2021-06-28
### Added
- #1: Smarter plugin selection screen which labels and prevents missing masters and other common loading issues.
- #2: Report missing plugins (i.e. targeted by the profile, but no longer in the load order) as warnings prior to build.
- #3: Mugshot synonyms (redirects) for differently-named mods.
- #5: New filters in Profile tab, including plugin selections.
- #14: Double-click on build warning to jump to profile row.
- #18: Option to reset only missing references in Maintenance tab.

### Changed
- #20: Ignore invalid NPC overrides (AKA "injected records") until a sane strategy for dealing with them can be developed.

## [0.2.1] - 2021-06-21
### Fixed
- #15: Fixed infinite-loop bug causing unrecoverable freeze while loading.

## [0.2.0] - 2021-06-20 [YANKED]
### Fixed
- App settings no longer reset with each new release.
- First-time profile significantly less likely to choose a visual overhaul as the default plugin.

## [0.1.2] - 2021-06-19
### Fixed
- Dark theme UI colors.

## [0.1.1] - 2021-06-19
### Fixed
- Fixed game detection on newer Steam installations.

## [0.1.0] - 2021-06-15
### Added
- Initial release with basic record-facegen sync. Profiles, build, settings, and high-level maintenance functions.

[Unreleased]: https://github.com/Alaxouche/EasyNPC-Next/compare/v1.1.0.0...HEAD
[1.1.0.0]: https://github.com/Alaxouche/EasyNPC-Next/compare/v1.0.0.0...v1.1.0.0
[1.0.0.0]: https://github.com/Alaxouche/EasyNPC-Next/compare/focustense-v0.9.6...v1.0.0.0
[0.9.6]: https://github.com/focustense/easymod/compare/v0.9.5...v0.9.6
[0.9.5]: https://github.com/focustense/easymod/compare/v0.9.4...v0.9.5
[0.9.4]: https://github.com/focustense/easymod/compare/v0.9.3...v0.9.4
[0.9.3]: https://github.com/focustense/easymod/compare/v0.9.2...v0.9.3
[0.9.2]: https://github.com/focustense/easymod/compare/v0.9.1...v0.9.2
[0.9.1]: https://github.com/focustense/easymod/compare/v0.9.0...v0.9.1
[0.9.0]: https://github.com/focustense/easymod/compare/v0.8.8...v0.9.0
[0.8.8]: https://github.com/focustense/easymod/compare/v0.8.7...v0.8.8
[0.8.7]: https://github.com/focustense/easymod/compare/v0.8.6...v0.8.7
[0.8.6]: https://github.com/focustense/easymod/compare/v0.8.5...v0.8.6
[0.8.5]: https://github.com/focustense/easymod/compare/v0.8.4...v0.8.5
[0.8.4]: https://github.com/focustense/easymod/compare/v0.8.3...v0.8.4
[0.8.3]: https://github.com/focustense/easymod/compare/v0.8.2...v0.8.3
[0.8.2]: https://github.com/focustense/easymod/compare/v0.8.1...v0.8.2
[0.8.1]: https://github.com/focustense/easymod/compare/v0.8.0...v0.8.1
[0.8.0]: https://github.com/focustense/easymod/compare/v0.3.0...v0.8.0
[0.3.0]: https://github.com/focustense/easymod/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/focustense/easymod/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/focustense/easymod/compare/v0.1.2...v0.2.0
[0.1.2]: https://github.com/focustense/easymod/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/focustense/easymod/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/focustense/easymod/tree/v0.1.0
