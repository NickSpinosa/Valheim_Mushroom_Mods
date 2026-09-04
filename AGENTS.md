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
| CombatAdustments | `docs/shield-rework-requirements.md`, `docs/feasts.md` |
| CraftableSpawners | `docs/design_decisions.md` |
| MushroomSync | `docs/DESIGN.md` |
| Separate Spawns | `docs/CONTEXT.md` |
| haldor-expansion | `docs/DESIGN.md` |
| RandomYggdrasil | none yet |
| vegvisir-compass | none — its design lives in the README's "How it works" |

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
