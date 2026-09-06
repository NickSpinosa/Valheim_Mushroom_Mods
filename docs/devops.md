# DevOps

How the mods get built and published, and how to check the pipeline still works
before you rely on it.

| | |
|---|---|
| [`.github/actions/build-mods`](../.github/actions/build-mods/action.yml) | Fetches the reference assemblies, builds every mod, and packages the DLLs. All the real logic lives here |
| [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) — **CI** | Compile check on every push and pull request |
| [`.github/workflows/release.yml`](../.github/workflows/release.yml) — **Build mod DLLs** | Same build, plus packaging the DLLs and attaching them to a release |

Both workflows call the same composite action, so a change to how the mods are
built lands in both at once. Only the release upload is workflow-specific.

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
| `ValheimManaged`, `BepInExCore` | MushroomSync, vegvisir-compass, RandomYggdrasil, SeparateSpawns |
| `ValheimDir` | CombatAdjustments, haldor-expansion |
| `GamePath` | CraftableSpawners |

Global properties beat anything a project sets itself, which is what overrides
the hardcoded local install paths (`F:\Steam\...`, Steam-registry lookups) that
resolve to nothing on a runner.

The whole tree is cached, and both workflows share one cache. Bump
`refs-cache-version` in the composite action to discard
it and refetch — worth doing after a Valheim update the mods need to build
against. A cold run takes roughly two minutes; a cached one about half that.

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

## Never commit

- Game assemblies (`assembly_*.dll`, `UnityEngine*.dll`) and BepInEx
  (`BepInEx*.dll`, `0Harmony*.dll`). Reference them from the local install; CI
  fetches its own.
- Decompiled game source. Keep it locally for looking up method names if you
  like — `Decompiled/` is gitignored.
- Build output. Releases carry the artifacts.
