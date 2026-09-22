# DevOps

How the mods get built and published, and how to check the pipeline still works
before you rely on it.

| | |
|---|---|
| [`.github/actions/build-mods`](../.github/actions/build-mods/action.yml) | Fetches the reference assemblies, builds every mod, and packages the DLLs. All the real logic lives here |
| [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) — **CI** | Compile check on every push and pull request |
| [`.github/workflows/release.yml`](../.github/workflows/release.yml) — **Build mod DLLs** | Same build, plus attaching the DLLs to a release and publishing each mod to Thunderstore |
| [`.github/scripts/`](../.github/scripts/) | The Thunderstore packaging and publish steps, as scripts so they run locally too |
| [`thunderstore/`](../thunderstore/) | One folder per mod: its Thunderstore description, categories, icon, and README pointer |

Both workflows call the same composite action, so a change to how the mods are
built lands in both at once. Only the uploads are workflow-specific.

## Compile checks

Pushes and pull requests run **CI** automatically — no action needed. It builds
every mod and fails on the first one that does not compile.

On a pull request it also **comments with a link to the build**, so a reviewer
can test the branch without hunting through the Actions tab. The artifact is
`MushroomMods-plugins.zip` — the same package a release ships — kept for 14
days.

Each push deletes the previous build comment and posts a new one, so there is
only ever one and it sits at the bottom of the thread. Editing in place was
tried first and reads as stale: GitHub anchors an edited comment at its original
position, so it ends up above later commits, describing what looks like an older
build.

The comment is found by a hidden `<!-- mushroom-build-link -->` marker rather
than by "the last comment the bot posted", so another workflow commenting cannot
make CI clobber the wrong one.

GitHub wraps every artifact in a zip of its own, so a downloaded build has two
layers to unpack: the artifact zip, then `MushroomMods-plugins.zip` inside it.

The comment is skipped for pull requests from forks. Those runs get a read-only
token, and attempting to comment would fail the job as though the build were
broken.

Documentation-only changes are skipped via `paths-ignore` (`**.md`, `docs/**`,
`LICENSE`), and a newer push to the same branch cancels an in-flight run.

## Smoke test

The pipeline has one throwaway **draft** release, `ci-test`, that exists purely
to be re-uploaded to. Run this after touching the workflow, adding a mod, or
changing how any mod resolves its references:

```bash
gh workflow run "Build mod DLLs" --ref main -f release_tag=ci-test
```

It builds every mod, packages them, and attaches the zip to that draft,
exercising the exact path a real release takes — including `gh release upload`,
which is otherwise only reached when a release is published. The upload uses
`--clobber`, so the same draft can be reused indefinitely.

Check the result:

```bash
gh run watch $(gh run list --workflow="Build mod DLLs" --limit 1 --json databaseId -q '.[0].databaseId')
```

```bash
gh release view ci-test
```

A draft release creates **no git tag** and is invisible to anyone browsing the
repo, so this costs nothing and pollutes nothing. If `ci-test` is ever deleted,
recreate it with:

```bash
gh release create ci-test --draft --title "CI smoke test" --notes "Not a real release."
```

To build without touching any release at all — useful when you only care that
the mods compile — dispatch with `release_tag` left blank. The zip still gets
built and comes out as a normal Actions artifact; only the upload is skipped:

```bash
gh workflow run "Build mod DLLs" --ref main
```

## Cutting a real release

Publishing a GitHub release fires the workflow on `release: [published]`, which
builds every mod and attaches one asset: **`MushroomMods-plugins.zip`**. No
built DLL is committed to the repo, so a downloaded plugin always corresponds to
a tagged commit.

The zip contains a single `plugins/` folder holding every mod's DLL, so
extracting it into a Valheim `BepInEx/` directory installs the lot in one step.
A release asset is always a file, never a directory — the zip is how a folder
gets attached.

The zip is built by the shared composite action, so a CI build and a release ship
byte-identical packaging.

Its entry names are written with explicit forward slashes. Both
`Compress-Archive` and `ZipFile::CreateFromDirectory` emit **backslashes** on
.NET Framework, which violates the zip spec; some extractors then produce one
file literally named `plugins\Foo.dll` instead of a folder. Runner `pwsh` is
new enough to get it right on its own, but the packaging step does not rely on
that.

The same release run then publishes each mod to Thunderstore. That is its own
section below, because it has its own rules about versions.

## Publishing to Thunderstore

Every mod is its own Thunderstore package, published under one team. The
release workflow does it after attaching the zip; a `workflow_dispatch` with
`publish_thunderstore` ticked does it on demand, for redoing an upload that
failed.

After publishing, the workflow appends an **On Thunderstore** table to the
release notes, one row per mod linking its package page and saying whether
this release uploaded it or it was already there. GitHub releases have no
comments, so the notes are where the links live. The section sits between
hidden `thunderstore-links` markers and is replaced, not duplicated, when the
workflow runs again for the same tag. Edit the notes above the markers freely;
anything inside them is overwritten.

### One-time setup

The workflow refuses to publish, with a warning rather than a failure, until
both of these exist in the repository settings:

1. **`THUNDERSTORE_NAMESPACE`** (Actions *variable*) — the team name on
   thunderstore.io. Create the team under your account first; the name is also
   the prefix of every package id (`Team-MushroomSync-1.0.0`).
2. **`THUNDERSTORE_TOKEN`** (Actions *secret*) — a **service account** token
   for that team, created on the team's settings page. It is read through the
   `TCLI_AUTH_TOKEN` environment variable and never appears on a command line.

The namespace is a variable, not a secret, because it is baked into every
manifest: a mod that depends on Mushroom Sync declares
`<namespace>-MushroomSync-<version>`, so the packaging step needs it too. CI
runs without it under a placeholder namespace, which is fine for checking that
packaging still works and useless for uploading.

### Versions come from the DLL, and must be bumped

The package version is the **`BepInPlugin` attribute's version** — the number
BepInEx logs at startup — read out of the built DLL by
[`.github/tools/PluginInfo`](../.github/tools/PluginInfo/Program.cs). It is not
the csproj `<Version>` (not every project keeps one) and not the release tag
(the mods do not share a version).

Thunderstore rejects a re-upload of an existing version. The publish script
therefore checks each package's version against the API first and **skips the
ones already there**, so a release that changed one mod uploads one package
and a release that changed nothing uploads nothing and says so. The flip side:
**a mod whose plugin version was not bumped does not reach Thunderstore**, even
if its code changed. Bump the version in the plugin's `BepInPlugin` attribute
as part of any change you want players to receive through a mod manager.
Thunderstore only accepts `Major.Minor.Patch`, and the packaging step fails on
anything else.

### What goes in a package

`thunderstore/<PackageName>/` holds, per mod:

- `package.json` — which DLL the package wraps, a description of at most 250
  characters, the repo-relative path of the README to ship, and the
  Thunderstore category slugs. Optional `dependencies` lists Thunderstore ids
  of mods outside this repo.
- `icon.png` — 256×256, the only size Thunderstore accepts. The current ones
  are placeholders drawn by a script; replace them with real art whenever.
- `README.md` — only for mods that have no README of their own; the others
  point `package.json` at the mod's README.

The folder name is the package name: letters, digits and underscores only, so
`CombatAdjustments` rather than `CombatAdjustments.ShieldRework`.

Dependencies between the repo's own mods are **derived from their
`BepInDependency` attributes**, not written down: the packager maps each GUID
to the package that provides it and pins that package's current version. A
`BepInDependency` on a GUID no package here provides fails the packaging step
with a message saying to add it under `dependencies`. `BepInExPack_Valheim` is
pinned to whatever Thunderstore lists as latest at packaging time, the same
resolution the build action uses to fetch it. Packages publish in dependency
order, so Mushroom Sync goes up before anything that requires it.

### Running it locally

```bash
.github/scripts/Build-ThunderstorePackages.ps1 -ArtifactsDir release-artifacts -OutDir thunderstore-packages -Namespace <team>
.github/scripts/Publish-ThunderstorePackages.ps1 -PackagesDir thunderstore-packages -DryRun
```

Both run under Windows PowerShell 5.1 as well as `pwsh`. The dry run writes the
`tcli` config for each package and prints what it would publish; drop `-DryRun`
with `TCLI_AUTH_TOKEN` set and `tcli` installed (`dotnet tool install -g tcli`)
to publish for real. `tcli publish --file` still wants a project config for the
namespace, community and categories, so the script generates one per package
in a temp directory. The `[build]` section in it is filler: `tcli` validates
that it exists even though `--file` means nothing gets built.

## How CI gets the game assemblies

The mods reference Valheim and BepInEx assemblies, which cannot be committed.
The runner therefore fetches its own:

- **Game assemblies** come from the **Valheim Dedicated Server** (Steam app
  `896660`), which is free and available to an anonymous `steamcmd` login. It
  ships the same managed assemblies the client does.
- **BepInEx** comes from the Thunderstore API for `denikson/BepInExPack_Valheim`,
  resolved at run time rather than pinned, so it tracks the current release.

Both are assembled into a **synthetic client install layout** under
`ci-refs/valheim-root/`:

```
ci-refs/valheim-root/
├── valheim_Data/Managed/    <- from the dedicated server
└── BepInEx/core/            <- from Thunderstore
```

The layout matters. Each mod invented its own name for the reference root, so
the workflow passes *every* spelling as an MSBuild global property, all pointing
at that one tree:

| Property | Used by |
|---|---|
| `ValheimManaged`, `BepInExCore` | MushroomSync, vegvisir-compass, RandomYggdrasil, SeparateSpawns, ResourceDropModifier |
| `ValheimDir` | CombatAdjustments, haldor-expansion |
| `GamePath` | CraftableSpawners |

Global properties beat anything a project sets itself, which is what overrides
the hardcoded local install paths (`F:\Steam\...`, Steam-registry lookups) that
resolve to nothing on a runner.

The whole tree is cached, and both workflows share one cache. Bump
`refs-cache-version` in the composite action to discard
it and refetch — worth doing after a Valheim update the mods need to build
against. A cold run takes roughly two minutes; a cached one about half that.
Neither workflow overrides the input, so bumping its default in the action is
the whole change. Last bumped to `v2` for the Valheim 1.0.7 update (the cache
still held 0.221 assemblies, so CI could not see the 1.0.7 signature changes).

## Adding a mod

Discovery is generic: every top-level directory is scanned for `.csproj`, so a
new mod directory needs no workflow change. It does need two things:

1. **Resolve its references through one of the property names above.** Give the
   property a local default so a bare `dotnet build` still works — the pattern
   used by most of the mods here consults the Steam registry first:

   ```xml
   <ValheimDir Condition="'$(ValheimDir)' == ''">$([MSBuild]::GetRegistryValueFromView('HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 892970', 'InstallLocation', null, RegistryView.Registry64, RegistryView.Registry32))</ValheimDir>
   ```

2. **Add any new reference to the verify step's list.** The workflow checks the
   union of every assembly the mods need before building, so a missing one
   produces a named failure instead of a wall of `CS0246`.

3. **Add a `thunderstore/<PackageName>/` folder** with `package.json` and a
   256×256 `icon.png`, as described under *Publishing to Thunderstore*. The
   packaging step fails the build for any built DLL without one, so a new mod
   cannot quietly miss Thunderstore.

Projects under `bin/`, `obj/`, `tools/` and `Decompiled/` are skipped — they are
build output, dev tooling, and decompiled game source respectively, none of them
shippable. Two projects emitting the same assembly name fail the build rather
than silently overwriting each other in the artifact folder.

## Building locally

Most mods build with no arguments, resolving the game path from the Steam
registry:

```bash
dotnet build -c Release
```

Two do not, and need the install passed explicitly:

- **CombatAdustments** defaults `ValheimDir` to `F:\Steam\steamapps\common\Valheim`.
- **haldor-expansion** errors unless you create `Local.props` from
  `Local.props.example`.

```bash
dotnet build haldor-expansion/HaldorExpansion.csproj -c Release -p:ValheimDir="C:\Program Files (x86)\Steam\steamapps\common\Valheim"
```

vegvisir-compass reads its path from `Directory.Build.user.props`, which is
gitignored — see its own README.

## After a Valheim update

The 1.0.7 update (September 2026) broke seven of the eight mods in ways that
did not follow from reading the patch notes, and the order the fixes went in
mattered. This is that order, and the reasons behind it. The tickets were
GitHub issues #5 through #25; each mod's own `docs/` holds the mod-specific
lesson, and this section holds the ones that apply to every mod.

**Bump `refs-cache-version` before opening a single fix PR.** CI builds against
cached game assemblies, so until the cache is discarded every fix branch is
compiled against the old game. A fix that only compiles on the new version
goes red, and a stale DLL that only fails on the new version goes green. It is a
one-line change; do it alone, first, and let it merge before anything else.

**Rebuild and re-release every mod, including the ones whose source still
compiles unchanged.** C# bakes optional-parameter defaults into the caller at
compile time. `Terminal.ConsoleCommand`'s constructor gained a
`hideBehindDevCommands` parameter in 1.0.7, so a DLL built against 0.221 called a
constructor overload that no longer existed and threw `MissingMethodException`
at runtime, even though the same source built against 1.0.7 without a change.
That particular throw happened inside `Console.Awake`, before the game hid the
console window, which is why a stale Combat Adjustments DLL left placeholder
console text covering the main menu. A green build of the old DLL proves
nothing; only a fresh build does.

**Harmony attributes that name parameter types are exact matches.** Adding an
optional parameter to a game method is source-compatible for the game and
breaking for a `[HarmonyPatch]` with an explicit `typeof` list: the attribute
resolves against the real signature at patch time and finds nothing. The source
still compiles. After an update, re-check every attribute in the repo that
lists types, not just the patches whose behaviour changed. The same goes for
reflection by type name; `PlatformManager` moved into the `Splatform` namespace
and a `GetType("PlatformManager")` quietly returned null.

**The first real signal is `BepInEx/LogOutput.log`, not the compiler.** Patch
failures, missing methods and null reflection lookups all show up there on the
first launch and nowhere else. Read it before doing anything else. Then
decompile the new `assembly_valheim.dll` (`ilspycmd -p -o <dir> -r <Managed>`
takes about a minute) and grep it for every Harmony target and every
reflection string the mods use. That found every 1.0.7 defect that a build
could not.

**One bad patch class must not take a plugin down.** `Harmony.PatchAll` lets
the first failing class escape, and the exception unwinds the rest of `Awake`,
so sync registration and console commands died with a tooltip patch that had
nothing to do with them. Every plugin now patches through
`Shared/PatchIsolation.cs`, last in `Awake`; see the Shared source section of
`AGENTS.md`.

**Zones and simulation distance changed shape.** Zone keys are `Vector2s` from
`assembly_utils`, not `Vector2i`, and the fixed `m_activeArea` and
`m_activeDistantArea` are gone in favour of a per-peer simulation distance that
is a player setting. Any mod that reasoned about "the zones around a player"
had to be re-derived; `HornOfCalling/docs/CONTEXT.md` and
`vegvisir-compass/docs/CONTEXT.md` have the two derivations.

Where the mod-specific lessons live:

| Lesson | Doc |
|---|---|
| `ZDOMan.GetPortals` now returns a per-sector dictionary; the flat list is `GetPortalList` | `Separate Spawns/docs/valheim-1.0-save-system.md` |
| Per-world save folders, `.fwl2` metadata, and what a seed reroll has to delete | `Separate Spawns/docs/valheim-1.0-save-system.md` |
| Resolving the local platform id on client and headless server | `Separate Spawns/docs/valheim-1.0-platform-id.md` |
| `TerrainOp` settings travel as a prefab hash resolved through ObjectDB | `Separate Spawns/docs/valheim-1.0-terrainop.md` |
| `TradeItem` fields Unity serialises non-null that a constructor leaves null | `haldor-expansion/docs/trade-item-1.0.7.md` |
| `GetTooltip` optional parameter and why one attribute took the plugin down | `CombatAdustments/docs/valheim-1.0.md` |
| Deep North item names and the ObjectDB dump that verifies them | `CombatAdustments/docs/valheim-1.0.md` |
| The new build UI and cloned prefab names | `CraftableSpawners/docs/valheim-1.0-buildui.md` |
| Whether the Yggdrasil branch scene object survives | `RandomYggdrasil/docs/yggdrasil-branch.md` |

## Things that look broken but are not

**`steamcmd attempt 1 produced no managed assemblies; retrying.`** — expected.
steamcmd exits `7` on its own self-update pass having downloaded nothing, so the
workflow judges success by whether the assemblies actually appeared and retries
once. Attempt 2 succeeds.

**`Download Valheim managed assemblies` / `Download BepInEx core` skipped** —
a cache hit. The reference tree was restored instead of refetched.

**`Attach the plugins folder to the release` skipped** — the run was a dispatch
with no `release_tag`. Only a published release or an explicit tag triggers the
upload.

**`Thunderstore publish skipped: set the THUNDERSTORE_TOKEN secret ...`** — the
one-time setup under *Publishing to Thunderstore* has not been done. The
release itself is fine.

**`No Thunderstore package had a new version.`** — every mod's plugin version
already exists on Thunderstore, so there was nothing to upload. If a mod did
change, its `BepInPlugin` version was not bumped.

**`No Thunderstore namespace given; packaging under placeholder`** — a CI run
without the `THUNDERSTORE_NAMESPACE` variable. The packages it builds are for
checking the step, not for uploading.

## Never commit

- Game assemblies (`assembly_*.dll`, `UnityEngine*.dll`) and BepInEx
  (`BepInEx*.dll`, `0Harmony*.dll`). Reference them from the local install; CI
  fetches its own.
- Decompiled game source. Keep it locally for looking up method names if you
  like — `Decompiled/` is gitignored.
- Build output. Releases carry the artifacts.
