# Working in this repo

A monorepo of BepInEx mods for Valheim, one mod per top-level directory, built
for a large no-map dedicated server.

## Docs

**Releasing, CI, or anything about the build workflows** — read
[docs/devops.md](docs/devops.md). It covers the compile check that runs on every
push, the `ci-test` release smoke test, how CI sources the game assemblies it
cannot commit, what a new mod must do to be discovered, and the log lines that
look like failures but are not.

Both workflows build through the same composite action,
[`.github/actions/build-mods`](.github/actions/build-mods/action.yml) — change
how the mods are built there, not in a workflow.

**After a Valheim update** — read the "After a Valheim update" section of
[docs/devops.md](docs/devops.md) before touching any mod. It is the order of
operations the 1.0.7 update taught: what to rebuild even when nothing looks
broken, where the first real signal is, and which kinds of change compile
clean and still fail at runtime.

**Working inside a mod directory** — read that mod's own docs before you touch
its code, and record what you learn there when you are done. The material that
earns a place: a Valheim or BepInEx API that behaves differently than its name
suggests, an approach that was tried and rejected and why, a trap that cost real
time to diagnose. Write down the reasoning, not the diff — git already has the
diff.

Every mod keeps that material in its own `docs/` directory. Create one if the
mod does not have it yet.

| Mod | Docs |
|---|---|
| CombatAdustments | `docs/shield-rework-requirements.md`, `docs/feasts.md`, `docs/sailing.md`, `docs/boss-hp-scaling.md`, `docs/valheim-1.0.md` |
| CraftableSpawners | `docs/design_decisions.md`, `docs/valheim-1.0-buildui.md` |
| MushroomSync | `docs/DESIGN.md` |
| Separate Spawns | `docs/CONTEXT.md`, `docs/spawn-priority.md`, `docs/world-lifecycle.md`, `docs/valheim-1.0-save-system.md`, `docs/valheim-1.0-platform-id.md`, `docs/valheim-1.0-terrainop.md` |
| haldor-expansion | `docs/DESIGN.md`, `docs/trade-item-1.0.7.md`, `docs/placeable-item.md` |
| HornOfCalling | `docs/CONTEXT.md` |
| RandomYggdrasil | `docs/yggdrasil-branch.md` |
| vegvisir-compass | `docs/CONTEXT.md`; the user-facing design lives in the README's "How it works" |

## Server-authoritative sync

Combat Adjustments, Craftable Spawners, Haldor Expansion and Random Yggdrasil
get their host-follows-client behaviour from **MushroomSync**, a shared plugin.
Read [MushroomSync/README.md](MushroomSync/README.md) before adding a synced
setting or a new push of server data, and
[MushroomSync/docs/DESIGN.md](MushroomSync/docs/DESIGN.md) before changing how
sync itself works.

The trap it exists to prevent: this was four copies of one implementation that
drifted, so a fix landed in one mod and not the others. Change it in
MushroomSync, not in a mod.

## Shared source (`Shared/`)

`Shared/` is not a mod and has no project of its own. It holds source files that
every plugin compiles into itself through a linked `<Compile Include>`, for
things all seven need identically but that do not justify a runtime dependency
on MushroomSync — the three mods that do not already reference it stay
standalone DLLs.

Right now that is one file, `PatchIsolation.cs`. **Every plugin applies its
Harmony patches through `PatchIsolation.PatchAllIsolated`, last in `Awake`, not
through `Harmony.PatchAll`.** `PatchAll` lets the first failing patch class
escape, and that exception unwinds the rest of `Awake` — in 1.0.7 one changed
`GetTooltip` signature took Combat Adjustments' console commands and config sync
with it (issues #5, #18). Ordering is the other half: config binding, sync
registration and coroutines go *above* the patch call.

Edit the file in `Shared/`; adding another plugin means adding the same
`<Compile Include>` line to its project. There is deliberately no second copy.

## Building

Most mods build with a bare `dotnet build -c Release`, resolving the game path
from the Steam registry. Each mod names its reference root differently, and two
of them need the path passed explicitly — [docs/devops.md](docs/devops.md) has
the property table and the exceptions.

## Never committed

Game assemblies (`assembly_*.dll`, `UnityEngine*.dll`), BepInEx
(`BepInEx*.dll`, `0Harmony*.dll`), decompiled game source, and build output.
Reference them from the local install instead; CI fetches its own copies, and
releases carry the built DLLs. This is the one mistake the repo has already had
to undo with a history rewrite, and `.gitignore` is the guard — if a change
requires loosening it, that is the signal to stop and ask.
