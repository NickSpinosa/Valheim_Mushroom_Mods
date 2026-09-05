# 08 — README, repo docs and release smoke test

**Goal:** the mod is documented the way the others are, wired into the repo's
index files, and proven to build and package through the real release path.

**Depends on:** 02 through 07 merged. **Unblocks:** a release.

## Deliverables

### `audible-horn/README.md`

Model it on `vegvisir-compass/README.md` and `MushroomSync/README.md`.
Sections, in order:

1. Title and one paragraph: what it is, for whom (no-map servers), what a
   player experiences. Use the glossary's terms.
2. **What it does** — bullets: craft at a workbench (recipe), equip and attack
   to sound, heard within Hearing Range with direction and distance, works
   seated / swimming / riding, cooldown with message, volume slider location,
   host-authoritative settings. Say plainly what it does not do: no text, no
   map marker, no monster reaction, every horn sounds the same.
3. **Requirements** — table: Valheim build, BepInEx version, MushroomSync
   (required, ships in the same zip). "Everyone needs it: every client and the
   dedicated server" with one sentence why (item prefab, RPC).
4. **Configuration** — the three entries from ticket 01 as a table, with which
   are host-set. Note that editing `HornVolume` is easier from Settings →
   Audio.
5. **Credits** — link to `CREDITS.md` for the recording.
6. **Building** — the one-line `dotnet build` command; link to
   `docs/devops.md` for CI.
7. **Design notes** — link to `docs/CONTEXT.md` (language) and
   `docs/DESIGN.md` (lessons).

### Root `README.md`

- Add a row to the Mods table:
  `| [Audible Horn](audible-horn/README.md) | Craft a Signal Horn and sound it; players within earshot hear where you are, no map needed |`
- Add `AudibleHorn.dll` to the `plugins/` listing in the install block, and
  add it to the sentence listing which mods need `MushroomSync.dll`.

### `MushroomSync/README.md`

Add a row to the **Required by** table: `| Audible Horn | Config sync |`.
Update the sentence in the root README that says "the four mods above" if it
counts them.

### `AGENTS.md`

Add a row to the Docs table: `| audible-horn | docs/CONTEXT.md, docs/DESIGN.md |`
and add Audible Horn to the "Server-authoritative sync" paragraph's list of
mods that use MushroomSync.

### `audible-horn/docs/DESIGN.md`

By now tickets 02–06 have each added a section. Read it top to bottom, give it
a two-line intro, and order sections in the sequence a new reader would need:
item cloning, input interception, networking, audio, settings UI. Remove any
section that only restates the code. Keep every "tried and rejected" note.

### Release smoke test

Follow `docs/devops.md` → Smoke test:

```bash
gh workflow run "Build mod DLLs" --ref audible-horn -f release_tag=ci-test
```

Then watch the run and confirm `MushroomMods-plugins.zip` on the `ci-test`
draft contains `plugins/AudibleHorn.dll` alongside `MushroomSync.dll`. Note
the run URL in the PR description when the maintainer opens one. If CI fails
on a missing reference assembly, the fix is in
`.github/actions/build-mods/action.yml`'s verify list, and that change gets
its own commit with a one-line explanation.

## Acceptance

- All four documents above updated; links resolve (open each one in the
  GitHub preview or a Markdown renderer).
- `ci-test` draft carries a zip with `plugins/AudibleHorn.dll`.
- Extracting that zip into a clean BepInEx install and joining the test
  server: Signal Horn craftable, sounds, heard by a second client. This is the
  final end-to-end check; do the seated, swimming and cooldown cases once more
  here since they are cheap.
- `git status` clean on `audible-horn`; no DLLs, no `Decompiled/`, no `bin/`
  committed (`git ls-files | grep -i '\.dll$'` returns nothing).
