# Audible Horn

A BepInEx mod for [Valheim](https://www.valheimgame.com/), built for **no-map
multiplayer playthroughs**.

Craft a **Signal Horn** at a workbench, hold it, and press attack. Everyone
within **Hearing Range** hears the **Horn Call** — from the direction you are
standing in, and quiet enough at the edge to tell you are far away. On a server
with no map that is the whole navigation system: someone lost blows the horn,
and the rest of the group walks toward it.

## What it does

- **Craft it at a workbench.** 4 bone fragments, 2 leather scraps, 1 resin, no
  upgrade level needed. It is a separate item cloned from Odin's drinking horn;
  vanilla tankards are untouched and stay craftable.
- **Equip it and press attack to sound it.** No stamina cost, no durability, and
  the horn is never consumed.
- **Every Listener within Hearing Range hears it**, positioned in 3D, so the
  sound has a direction. Loudness falls off *linearly* from full at the Blower to
  silence at the edge, which is what lets a Listener judge rough distance.
  Terrain and buildings do not block or muffle it.
- **The sound travels with the Blower** while it lasts, so a Listener hears a
  moving boat rather than its wake — as long as the Blower is close enough for
  this client to have them loaded. Further away it plays from the position the
  call was made at.
- **Seated, swimming and riding all work.** Sitting at a rudder or a bench,
  swimming, and riding a lox each sound the horn without standing you up or
  dismounting you. The drinking animation plays only when your body is free; the
  Horn Call goes out either way.
- **Horn Cooldown** — one Blower may sound the horn once every 10 seconds by
  default. An early press gets a centre-screen *"The horn still rings."* and
  nobody hears anything.
- **A Signal Horn slider in Settings → Audio**, next to Effects and Music. It is
  a personal loudness multiplier, applied on top of the game's own Effects
  volume, and it behaves like the vanilla sliders: OK saves it, Back reverts it.
- **The host decides Hearing Range and Horn Cooldown**, and every client follows.
  There is no opt-out, deliberately — see
  [Configuration](#configuration).

What it deliberately does **not** do:

- No chat message, no text, no notification of any kind.
- No map marker, no ping, no arrow. The map is never touched.
- No monster reaction. A Horn Call does not aggro, attract or scare anything.
- No way to tell **who** blew a horn, or how many people did. Every Horn Call
  sounds exactly the same. Hearing it is the entire effect.

## Requirements

| | |
|---|---|
| Valheim | Unity `6000.0.61f1`, build `21981559` |
| BepInEx | `5.4.2333` ([BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)) |
| [Mushroom Sync](../MushroomSync/README.md) | **Required.** `MushroomSync.dll` ships in the same release zip |
| .NET SDK | 9.0 — to build only; players need nothing beyond BepInEx |

No Jotunn, no ServerSync, no ConfigurationManager.

**Everyone needs it — every client and the dedicated server.** The Signal Horn is
a real item prefab, so a client without the mod does not have the item at all;
and a Horn Call is a routed RPC that the server relays, so a client without the
mod is never a Listener. The same DLL runs on the server, where it registers the
prefab and passes calls along without playing anything.

## Installing

Releases ship every Mushroom mod together. Download
**`MushroomMods-plugins.zip`** from the [latest release](../../releases/latest)
and take both `AudibleHorn.dll` and `MushroomSync.dll` out of its `plugins/`
folder:

```
Valheim/
└── BepInEx/
    └── plugins/
        ├── AudibleHorn.dll
        └── MushroomSync.dll
```

Do this on **every client and on the dedicated server**. For the server that is
its own Valheim directory — the folder holding `valheim_server.exe` and its
`BepInEx/`.

The config file appears on first launch at
`BepInEx/config/mushroom.audiblehorn.cfg`.

## Configuration

| Setting | Default | Decided by | Notes |
|---|---|---|---|
| `Horn.HearingRange` | `100` | **Host** | Metres from the Blower beyond which a Horn Call is silent. Straight-line; terrain does not block it. Range 10–2000 |
| `Horn.Cooldown` | `10` | **Host** | Seconds a player must wait between their own Horn Calls. `0` means no cooldown. Range 0–120 |
| `Audio.HornVolume` | `1.0` | Player | Personal loudness multiplier for Horn Calls, on top of the game's Effects volume. Never shared. Range 0–1 |

The two host settings have **no opt-out gate** — no `GatedBy`, no
`AcceptedWhen` — and that is deliberate rather than an oversight. Hearing Range
and Horn Cooldown are not preferences, they are a shared fiction between two
machines: a Listener with a longer range hears a call the Blower's client
believes was out of earshot, and a client with a shorter cooldown sounds the horn
more often than the server allows. Either way two players disagree about what
just happened, which is the one failure a mod whose entire feature is *hearing
the call* cannot tolerate. The reasoning is written out in
[docs/DESIGN.md](docs/DESIGN.md#why-sync-has-no-opt-out-gate).

`HornVolume` is the opposite case and is excluded from sync entirely: it changes
nothing another player observes, and a host overwriting it would be an intrusion.
**Editing it by hand is the hard way** — open **Settings → Audio** and use the
*Signal Horn* slider instead; it writes the same setting.

### Debug commands

Three cheat-only console commands ship with the mod. They need `devcommands`
first, so they cannot be used to make noise on a server with cheats off.

| Command | What it does |
|---|---|
| `hornsound` | Plays a Horn Call 5 m in front of you. Local only — nothing is sent |
| `hornsoundfollow` | Plays a call parented to you, so you can walk while it sounds |
| `horncall` | Broadcasts a real Horn Call from your position, exactly as sounding the horn does, ignoring the Horn Cooldown |

Every Horn Call sent, received, played or skipped logs one Info line to
`BepInEx/LogOutput.log`, with the distance computed and the reason it was
skipped. Those lines are how the range, cooldown, seated and swimming behaviour
is checked without having to hear anything.

## Credits

**The shipped horn sound is a placeholder, not a recording.** It is a synthesised
tone — a 2.5 s additive sine at 220 Hz with two harmonics — generated by
[`tools/make-placeholder-horn.ps1`](tools/make-placeholder-horn.ps1), which is
committed so the file is reproducible. It is meant to sound obviously synthetic
so nobody mistakes it for the final asset.

A real recording is still to be sourced, under
[docs/tickets/07-sound-asset.md](docs/tickets/07-sound-asset.md), and a
`CREDITS.md` naming its title, author, source, licence and the conversion applied
will land with it. Until then there is nothing to attribute: no third-party audio
ships in this mod.

## Building

```
dotnet build audible-horn/AudibleHorn.csproj -c Release
```

No arguments needed: the game path is resolved from the Steam registry. Add
`-p:CopyToPlugins=true` to install the DLL straight into the local client's
`BepInEx/plugins`.

Game assemblies are referenced from the local install and never committed. CI
fetches its own copies and builds every mod the same way — see
[docs/devops.md](../docs/devops.md).

## Design notes

- [docs/CONTEXT.md](docs/CONTEXT.md) — the glossary. Signal Horn, Horn Call,
  Blower, Listener, Hearing Range, Horn Cooldown, Horn Volume are defined there
  and used with those meanings everywhere else.
- [docs/DESIGN.md](docs/DESIGN.md) — what was learned building it: why the
  attack is intercepted in `Player.SetControls` and nowhere else, why ZSFX was
  rejected, how the SFX mixer group is found, and the two workarounds
  `UnityEngine.AudioModule` costs a net472 project.
- [docs/tickets/](docs/tickets/) — the build plan, one ticket per piece.

## License

Released under the [MIT License](../LICENSE).
