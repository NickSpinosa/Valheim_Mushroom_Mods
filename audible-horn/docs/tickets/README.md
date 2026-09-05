# Audible Horn — build tickets

The design is settled in [../CONTEXT.md](../CONTEXT.md). Read it first; the
tickets use its terms (Signal Horn, Horn Call, Blower, Listener, Hearing Range,
Horn Cooldown, Horn Volume) without redefining them.

Each ticket is self-contained enough for an agent to pick up cold. Work them in
the order below; the dependency column says what must be merged first.

| # | Ticket | Depends on |
|---|---|---|
| 01 | [Project scaffold and config](01-scaffold.md) | — |
| 02 | [Signal Horn item and recipe](02-item-and-recipe.md) | 01 |
| 03 | [Embedded horn sound and local playback](03-sound-playback.md) | 01 |
| 04 | [Horn Call over the network](04-horn-call-rpc.md) | 03 |
| 05 | [Sounding the horn: input, cooldown, seated and swimming](05-blow-input.md) | 02, 04 |
| 06 | [Horn Volume slider in the vanilla audio settings](06-volume-slider.md) | 01 |
| 07 | [Source the horn recording](07-sound-asset.md) | — (needs maintainer approval) |
| 08 | [README, repo docs and release smoke test](08-docs-and-release.md) | 02–07 |

03 and 04 can proceed with the synthesised placeholder clip from 03 until 07
lands.

## Conventions every ticket follows

These come from the rest of the repo and from `AGENTS.md`. They are not
optional.

- **Build:** `dotnet build audible-horn/AudibleHorn.csproj -c Release` must pass
  with no arguments on a machine with Steam Valheim installed, and in CI via the
  `ValheimDir` property. See `docs/devops.md`.
- **No Jotunn, no ServerSync, no ConfigurationManager.** MushroomSync is the
  only plugin dependency. Read `MushroomSync/README.md` before touching sync.
- **Never commit** game assemblies, BepInEx, decompiled source or build output.
  `.gitignore` already blocks them; do not loosen it.
- **Harmony patches on shared game hooks** (`ObjectDB.Awake`,
  `ObjectDB.CopyOtherDB`, `ZNetScene.Awake`, `Game.Start`) run at
  `HarmonyPriority(Priority.First)` and wrap their body in `try/catch` that logs
  and swallows, so a throw never aborts another mod's postfix chain. Copy the
  shape in `vegvisir-compass/src/Patches.cs`.
- **Private game members** are reached with `HarmonyLib.AccessTools` (field
  refs or `Traverse`), not by adding the assembly publicizer to this project.
- **Logging:** one `LogInfo` line per registration or start-up milestone,
  `LogWarning` for anything a server admin should notice, `LogError` with the
  exception for failures. No per-frame logging.
- **Dedicated server:** every code path must tolerate `Player.m_localPlayer ==
  null`, no `AudioListener`, and no UI. The server runs the same DLL.
- **Docs:** when a ticket teaches you something about the game or BepInEx that
  cost time to learn (an API that behaves unlike its name, an approach that was
  tried and failed), write it in `audible-horn/docs/DESIGN.md` under a short
  heading. Reasoning, not diffs. Create the file on first use.
- **Decompiled source** for looking up game methods lives locally under
  `CombatAdustments/decompiled/` (partial) and is gitignored. Member names
  quoted in these tickets were read from `assembly_valheim.dll` of the current
  game build; re-check if the game has updated.
- **Commit** per ticket on the `audible-horn` branch with a message that names
  the ticket. Do not merge or open a PR; the maintainer does that.

## Test environment on this machine

| | Path |
|---|---|
| Valheim client | `E:\Games\Steam\steamapps\common\Valheim` (BepInEx installed, other mods present in `BepInEx\plugins`) |
| Dedicated server | `E:\Games\Steam\steamapps\common\Valheim dedicated server` (**no BepInEx yet** — the maintainer installs it; do not download it yourself) |
| Local install of a build | `dotnet build audible-horn/AudibleHorn.csproj -c Release -p:CopyToPlugins=true` copies the DLL into the client's `BepInEx\plugins`. Copy it and `MushroomSync.dll` to the server's `BepInEx\plugins` by hand. |
| Server launch | `start_headless_server.bat` in the server directory, edited for a test world name and password; with BepInEx installed use the `start_server_bepinex` script the pack provides instead. |
| Client join | Run the client; Join Game → Add server → `127.0.0.1:2456`. |
| ffmpeg | not installed |

An agent cannot hear audio. What an agent can verify is what the logs say, so
tickets 03–05 must log, at Info, one line per Horn Call received with the
distance computed and whether it was played or skipped, and one line per call
sent. Those lines are the acceptance evidence for the range, cooldown, seated
and swimming cases. Panning, loudness and "sounds like a horn" are the
maintainer's to confirm by ear; list them as "human check" in the ticket's
report rather than claiming them.

Testing with two clients on one machine: a second Valheim client cannot run
from the same Steam account. The realistic agent setup is dedicated server +
one client, which covers the RPC path, the server relay, the distance gate
(use the `HearingRange` config and the debug command at a known offset), and
all input cases. The two-client hearing test is a human check.
