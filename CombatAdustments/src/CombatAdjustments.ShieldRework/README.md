# Combat Adjustments — Shield Rework

BepInEx plugin implementing the shield stagger / durability / tower block-armor
rework described in [`docs/shield-rework-requirements.md`](../../docs/shield-rework-requirements.md).

## What it does

| Change | Towers | Round shields | Bucklers |
| --- | --- | --- | --- |
| Flat stagger grant (equipped) | yes (Flametal **+70** max) | yes (Flametal **+45** max) | yes (Carapace **+20** max) |
| +5% block armor (ceil) | yes | no | no |
| +20% durability (ceil to 5) | yes | yes | no |
| Orange tooltip `Stagger: +N` | yes | yes | yes (if grant &gt; 0) |

Tower grants are seeded from **post-block leftover** against each shield's native
medium hit (normalized to Flametal +70), not block armor alone. Round/buckler
grants use block-armor ratios from their anchors, then snap to the **nearest 5**.
Every grant is overridable in `BepInEx/config` (client) or `config/bepinex` (dedicated server).

### Two-handed melee

- Greatswords, battleaxes, and sledges receive **Balanced** hyper armor: it
  begins when the real attack animation begins and ends after that swing's hit
  event. During that window stagger gain is blocked. Incoming damage and
  knockback are unchanged. Tooltips show orange `Hyper-armor`.
- Greatsword primary-chain swings deal **1.5x** stagger.
- Greatswords, battleaxes, and sledges deal **+10% damage** (bonus rounded down
  per damage type; reflected on weapon tooltips). Atgeirs are unchanged.
- Two-handed club ground slams (Stagbreaker, Iron Sledge, Demolisher) grant
  **adrenaline per enemy hit**, matching swing attacks (vanilla pays area
  attacks once per slam).

### Feasts

Biome feasts unlock after the **previous** biome boss instead of that biome's
own boss (Plains after Moder, not Yagluth). Sailor's Bounty still needs a
serpent. See [`docs/feasts.md`](../../docs/feasts.md).

Every feast also gains extra food stats (configurable; defaults below). Tooltips
and already-eaten feasts pick up the new numbers.

| Feast | Extra health | Extra stamina | Extra eitr |
| --- | --- | --- | --- |
| Most feasts | +10 | +10 | — |
| Sailor's Bounty | +15 | +15 | — |
| Mistlands | +10 | +10 | +7 (33 → 40) |
| Ashlands | +10 | +10 | +12 (38 → 50) |

Boss unlocks are **not** configurable.

### Sailing

Calm wind (**0–60%**) keeps the vanilla intensity ramp (**0.287 → 0.7**). Above
60% the factor keeps climbing linearly to **2.0** at full storm. Ocean
ThunderStorm chance is raised from ~**7%** to **21%**. See
[`docs/sailing.md`](../../docs/sailing.md).

### Difficulty scaling

Effective enemy HP keeps scaling with nearby players past vanilla’s **5**-player
cap (+30% per extra player). Enemy damage dealt stays capped at 5. Toggle with
`Difficulty.EnableUncapHealthScaling` (default **true**). See
[`docs/boss-hp-scaling.md`](../../docs/boss-hp-scaling.md).

## Install

1. Requires [BepInEx 5](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/) for Valheim.
2. **Requires `MushroomSync.dll`**, the shared server-authoritative sync plugin.
   This mod declares a `[BepInDependency]` on it and **will not load without it**.
3. Build: `dotnet build src/CombatAdjustments.ShieldRework -c Release` — this also
   builds MushroomSync, which it references.
4. Copy **both** into `Valheim/BepInEx/plugins/`:
   - `src/CombatAdjustments.ShieldRework/bin/Release/CombatAdjustments.ShieldRework.dll`
   - `MushroomSync/bin/Release/MushroomSync.dll`

   Or take the release's `MushroomMods-plugins.zip`, which carries both.
5. Launch once to generate `BepInEx/config/Abortipus.CombatAdjustments.ShieldRework.cfg`
   (or place server settings in `config/bepinex/Abortipus.CombatAdjustments.ShieldRework.cfg`).

## Config

Config is read from **both** locations (game root relative):

| Path | Used by |
| --- | --- |
| `BepInEx/config/Abortipus.CombatAdjustments.ShieldRework.cfg` | **Client** — primary load/save path |
| `config/bepinex/Abortipus.CombatAdjustments.ShieldRework.cfg` | **Dedicated server** — primary load/save path |

On clients, if both files exist, `config/bepinex/` values overlay the client file (overlay wins).
Dedicated servers (`-batchmode`) only use `config/bepinex/` unless that file is missing.

- `General.EnableStaggerGrant` / `EnableTowerArmorBonus` / `EnableDurabilityBonus`
- `General.SyncConfigInMultiplayer` (default **true** on host/server) — pushes settings to
  joining clients at runtime without overwriting their local `.cfg`
- `Tooltip.StaggerColorHex` (default `#E85AC8`)
- `StaggerGrants.<PrefabName>` — per-shield **max-quality** grant. Lower★ add
  **+1** (wood–iron), **+2** (silver/serpent–carapace), or **+3** (flametal) for
  towers/rounds; **bucklers always +1 / ★**. `GrantTableVersion` re-seeds max
  values when the designed table changes.
- `Two-Handed Combat.Enable` / `GreatswordPrimaryStaggerMultiplier`
  / `AreaAdrenalinePerEnemy`
- `Feasts.EnableStatBonuses` / `HealthBonus` / `StaminaBonus` /
  `SailorsHealthBonus` / `SailorsStaminaBonus` / `MistlandsEitrBonus` /
  `AshlandsEitrBonus`. Unlock bosses are hardcoded.
- `Sailing.EnableWindCurve` / `CalmForceFactor` / `KneeForceFactor` /
  `MaxForceFactor` / `CalmWindCeiling` — calm matches vanilla to 60%, then
  storms ramp to MaxForceFactor (see `docs/sailing.md`).
- `Sailing.EnableOceanStormChance` / `OceanThunderStormChance` — Ocean
  ThunderStorm target chance (default **21%**, vanilla ~7%).
- `Difficulty.EnableUncapHealthScaling` — keep effective enemy HP scaling past
  5 nearby players (default **true**). Enemy damage stays capped.

### Multiplayer config sync

When `General.SyncConfigInMultiplayer` is **true** on the host or dedicated server:

- The server sends its effective settings to each client on join (and again when the
  host changes config while clients are connected).
- Clients use those values for gameplay and tooltips at runtime; their local
  `BepInEx/config/*.cfg` is **not** modified.
- Clients can opt out locally with `SyncConfigInMultiplayer = false` (falls back to
  local config).
- Host and clients must use the **same mod version**; a mismatch shows a warning and
  keeps local config.

Install the mod on the **server and every client**. Only the server/host `.cfg`
needs to be tuned for gameplay balance.

## Console

| Command | What it does |
| --- | --- |
| `shieldstagger` / `sstagger` | Print stagger breakdown to the console |
| `staggerhud` / `shud` | Toggle HUD text under the stagger bar (`on` / `off` optional). Keeps the bar visible while on. |

No `devcommands` required.

## Implementation notes

- Stagger: Harmony postfix on `Character.GetStaggerTreshold` adds the equipped
  shield grant. `UpdateStagger` is replaced for players so drain uses the full
  threshold (vanilla drains `maxHP * factor / 5` and would ignore the grant).
- Equip/unequip (and R) **preserves stagger %** across the grant change so
  hide/draw cannot dump the bar.
- Armor/durability mutate `ItemDrop` shared data when ObjectDB loads, from cached
  vanilla originals so re-entry is idempotent.
- Feast food stats mutate the same shared data. Spice unlocks rewrite Bog Witch
  `m_requiredGlobalKey`; Black Forest / Swamp recipes are extra-gated because they
  share Woodland Herb Blend with Meadows.
- Sailing: Harmony prefix on `Ship.GetSailForce` swaps the intensity factor for
  the calm/storm curve when `Sailing.EnableWindCurve` is on. Ocean ThunderStorm
  weight is rewritten in `EnvMan.InitializeBiomeEnvSetup` to hit
  `OceanThunderStormChance`.
- Difficulty: Harmony postfix on `Game.GetDifficultyDamageScaleEnemy` recounts
  nearby players without the 5-player clamp when
  `Difficulty.EnableUncapHealthScaling` is on. Damage scaling is unchanged.
- Two-handed +10% damage must take `HitData.DamageTypes` by **ref**. It is a
  struct; the first implementation mutated a copy, so tooltips and hits stayed
  at vanilla numbers (iron sledge 55).
