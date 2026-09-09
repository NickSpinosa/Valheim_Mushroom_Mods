# Valheim 1.0.7 fix tickets: what blocks what

Temporary. Covers GitHub issues #5 through #19, filed 2026-09-09 after the
Valheim 1.0.7 update. Delete this file, and the pointer to it in `AGENTS.md`,
when #19 closes.

Read this before starting any of those tickets. The order matters more than
usual because CI is currently building against the wrong game version.

## The one hard gate: #9 step 1

Bumping `refs-cache-version` in `.github/actions/build-mods/action.yml` must
merge to main before any other fix is opened as a PR. CI still builds against
the cached 0.221 assemblies. The fixes for #6, #7 and #8 compile against 1.0.7
but **fail** against 0.221, so their PRs go red until the cache is bumped. It is
a one-line change. Do it alone, first.

## Blocking relationships

| Ticket | Blocked by | Why |
|---|---|---|
| #5 Combat Adjustments tooltip | #9 step 1 | CI green only against 1.0.7 |
| #6 Vegvísir Vector2s | #9 step 1 | Same |
| #7 Separate Spawns GetPortals | #9 step 1 | Same |
| #8 Separate Spawns seed reroll | #9 step 1, and #7 for a green build | Same project; neither branch compiles alone until both compile fixes are in |
| #10 Haldor null effects | #9 step 1 | CI, and needs the 1.0.7 client to reproduce |
| #9 step 2 (release) | #5, #6, #7, #8, #10 | These are the Critical and High fixes the release exists to ship |
| #12 Separate Spawns platform id | #7, #8 to verify | Code can be written now, but the mod cannot run until it builds |
| #13 Vegvísir key side effects | #6 to verify | Same reason, same project |
| #17 Deep North coverage | #5 to verify, plus an in-game item dump | The tables only matter once the plugin starts cleanly |
| #18 Patch isolation | #5 and the release | Touches every plugin's `Awake`; landing it during the hotfix window invites conflicts with #5 and #9 step 3 |
| #19 Docs | Everything else | Records what the fixes taught |

Nothing else has a code dependency. #11, #14, #15 and #16 are free-standing.

## What can run in parallel

**Wave 0, one person, about ten minutes.** #9 step 1. Merge it.

**Wave 1, up to five lanes at once, all branched from the bumped main.**

- **Lane A, Combat Adjustments:** #5. Two attribute edits.
- **Lane B, Vegvísir Compass:** #6, then #13 on the same branch or a second
  one. Different files, no conflict either way.
- **Lane C, Separate Spawns:** #7 and the two compile lines of #8 together on
  one branch so the project builds, then the real #8 rewrite and re-test of the
  reroll. #12 can sit on a side branch off this one. This lane is the critical
  path; the reroll rewrite is the only ticket that is real work rather than a
  signature fix.
- **Lane D, Haldor Expansion:** #10 and #11 together. Adjacent files, both
  tiny.
- **Lane E, no code changes needed:** #14 and #15 are in-game checks. Both
  mods already build on 1.0.7, so a local build can be tested today without
  waiting for anyone. #16 is a doc edit and can go any time.

**Wave 2, release.** #9 step 2 once lanes A, B (#6 only), C (#7 and at least
the #8 compile fix) and D (#10) are merged. Run `ci-test`, then the real
release. If the #8 rewrite is not finished, ship with the compile fix and a
known-issue note; the reroll only fires on a brand-new infeasible world.

**Wave 3, after a 1.0.7 server is running the release, all parallel.**

- Verification passes for #12 and #13.
- #17, which needs the in-game item dump and then table edits.
- #18 patch isolation, folding #9 step 3 (the console-command try/catch) into
  it so the hardening lands once.
- #19 docs, last, since it summarises the rest. Closing it removes this file.

## Critical path

#9 step 1 → #7 + #8 → release → server verification → #19. Everything else
hangs off that line. If only one person is working, the order is #9 step 1,
#5, #10, #6, #7, #8, release, then the rest.
