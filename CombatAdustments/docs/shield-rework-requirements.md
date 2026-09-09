# Shield Rework — Requirements

Status: design locked, **v0.1.0 implemented** (`src/CombatAdjustments.ShieldRework`).
Decided in the blocking/stagger design sessions of Aug 6, 2026.
Interactive balance sandbox: `charred-warrior-stagger.canvas.tsx` (Cursor canvas).
Mechanics verified against the local `assembly_valheim.dll` (Aug 2026); decompiled
snippets stay out of the repo (see `.gitignore`).

Tooltip stagger line uses the same `<color=orange>` as other item stats (block armor,
etc.). The HUD stagger bar is also orange.

## 1. Design goals

- Make hold-blocking (tower and round shields) a viable playstyle against crowds of
  light-to-medium hits, while keeping bucklers the best pure-parry option.
- Big attacks must still break blocks: players should be forced to dodge them. The
  vanilla armor formula's linear/quadratic crossover at `damage = 2 x blockArmor`
  enforces this automatically as long as block armor increases stay small.
- **Core principle: stagger must not be the ONLY limiting factor on a defensive
  hold.** (In vanilla it always is — even best-in-slot tanks stagger within a few
  blocked light hits, with health and stamina barely touched.) Instead, the
  player's food choices decide what ends the hold:
  - Ignore health food -> stagger (and the health behind it) is the limiting factor.
  - Ignore stamina food -> stamina is the limiting factor.
  - Balanced loadout (e.g. 2 health + 1 stamina) -> stamina generally empties
    before health at the Hard balance target; this is the intended sweet spot,
    not a hard requirement across all difficulties/pressure levels.
  - Trade-off summary: more health = survive bigger hits; more stamina = block
    more hits. Both must remain real choices.
- Food diversity: blockers should not be forced into 3 health foods. The flat (not
  HP-coupled) stagger grant achieves this. Full stamina stacking self-punishes
  (verified in sandbox: 3 stamina foods die by stagger cascade with half their
  stamina unspent) — this is by design, per the principle above.
- Melee is the weakest playstyle overall; modest buckler stagger is acceptable
  even though bucklers are already the best melee defensive option in vanilla.

## 2. Requirements

### R1 — Flat stagger-bar grant on shields

- Tower shields, round (regular) shields, **and bucklers** grant a **flat** addition
  to the player's stagger bar (`Character.GetStaggerTreshold`). Explicitly **not** a
  multiplier and **not** coupled to max HP: bar = `0.4 x maxHP + grant`.
- Granted while the shield is **equipped** (not only while actively blocking).
- Blocking-capable weapons grant nothing (2H weapon blocking will be evaluated
  separately later).
- Grant quality steps are small and even toward the max-quality table value.
  Applies to **towers and rounds** with the same progression bands; **bucklers
  always +1 / ★**:
  - **+1 / ★** — wood through iron (wood/banded rounds, bone/iron towers, …)
  - **+2 / ★** — silver / serpent through carapace (silver/black metal/carapace
    rounds, serpent/black metal towers, …)
  - **+3 / ★** — flametal (tower + round)
  - **Bucklers: always +1 / ★** (bronze, iron, carapace — e.g. carapace +20 over
    4★ → **+17 / +18 / +19 / +20**)
  - Example (4★): iron tower +25 → **+22 / +23 / +24 / +25**; flametal round +45 →
    **+36 / +39 / +42 / +45**.
- Values are **table-driven** per shield (config = max-quality grant).
- Anchors (fully upgraded / max quality):
  - **Flametal tower shield = +70**
  - **Flametal shield (round) = +45**
  - **Carapace buckler = +20**
- Parry identity: round shields may approach buckler max-parry capacity via the
  larger bar, but bucklers must still mitigate more damage per parry (higher
  effective block armor via 2.5x). Sandbox at skill 60 / 2HP+1stam: buckler +20
  safe max ~351 vs Flametal round +45 safe max ~348 — buckler slightly ahead on
  ceiling, clearly ahead on per-hit leftover (~43 vs ~52 on a Hard swing).
- Vanilla base stagger factor (40% of max HP) is unchanged (earlier proposal to
  lower it was rejected: it would nerf mages/archers/2H builds and create stunlock
  spirals for non-blockers).

#### Tower grant seeding (leftover-based)

Block armor alone understates mid-game pressure: enemy medium hits rise ~15× from
Meadows to Ashlands while tower block armor rises only ~7×. What fills the stagger
bar while holding is **leftover after block armor**, so tower grants are seeded from:

`grant ≈ 70 × leftover(nativeMediumHit, towerBlock) / leftover(150, 152)`

where leftover uses the vanilla armor formula. Native medium hits: wood/greydwarf 14,
bone/draugr 48, iron/draugr elite 58, serpent/fenring 85, black metal/seeker claw 120
(Mistlands — still the best tower until Flametal), flametal/warrior swing 150.

| Shield | Max block | Native medium hit | Leftover | Seed grant |
| --- | --- | --- | --- | --- |
| Flametal tower shield | 152 | 150 | ~37 | **+70** (anchor) |
| Black metal tower shield | 116 | 120 | ~31 | **+55** |
| Serpent scale shield | 72 | 85 | ~25 | **+50** |
| Iron tower shield | 64 | 58 | ~13 | **+25** |
| Bone tower shield | 44 | BF brute ~30 (hand-tuned) | ~5 | **+15** |
| Wood tower shield | 22 | 14 | ~2 | **+5** |

(For comparison, pure block-armor ratio at 70/152 would give BM +53, serpent +33,
iron +29, bone +20, wood +10 — leftover seeding raises serpent/BM for the Mountain–
Mistlands damage spike and lowers wood.)

Round / buckler grants still use block-armor ratios from their anchors (round ≈
45/126 × block, buckler ≈ 20/90 × block), then **rounded to the nearest 5**.

| Shield | Max block armor | Raw seed | Grant (nearest 5) |
| --- | --- | --- | --- |
| Flametal shield (round) | 126 | 45 | **+45** (anchor) |
| Carapace shield (round) | 108 | 39 | **+40** |
| Black metal shield (round) | 90 | 32 | **+30** |
| Silver shield (round) | 72 | 26 | **+25** |
| Banded shield (round) | 54 | 19 | **+20** |
| Wood shield (round) | 18 | 6 | **+5** |
| Carapace buckler | 90 | 20 | **+20** (anchor) |
| Iron buckler | 40 | 9 | **+10** |
| Bronze buckler | 28 | 6 | **+5** |

### R2 — Small block armor bonus, tower shields only

- Tower shields: **+5% block armor, rounded up** (Flametal tower: 152 -> 160).
- Round shields and bucklers: **no** block armor change (parry bonuses would
  multiply any round/buckler armor buff).
- Rationale for keeping it small: every point of effective block armor moves the
  must-dodge crossover up by 2 damage, and blocking skill multiplies the bonus
  (x1.5 at skill 100). This is the anti-stat-stick lever: if players equip tower
  shields only for the grant, shift budget from grant to block armor.

### R3 — Tooltip

- Shield tooltips must show the stagger grant when non-zero (e.g. `Stagger: +70`).
- Text uses the same orange color as other item stats (`<color=orange>`), matching
  block armor / the HUD stagger bar.
- Applies to tower shields, round shields, and bucklers with a grant. Omit the
  line when grant is 0.

### R4 — Durability increase

- Tower and round shields: **+20% max durability, rounded up to the nearest 5 or 10**.
- Rationale: hold-blocking absorbs many more hits per fight under this rework, so
  durability drain per outing rises.
- Examples: Flametal tower 300 -> 360; Serpent scale 350 -> 420 (values at max
  quality; apply to base durability + per-level bonus consistently).
- Bucklers unchanged (parry playstyle does not drive the same durability pressure).

### R5 — Explicitly unchanged

- Player base stagger factor (0.4), stagger drain rate (threshold/5 per second),
  stagger stun behavior.
- Block stamina drain formula (25 x absorbed/blockPower) — stamina remains a real
  limiter for hold-blocking; do not touch.
- Parry mechanics, parry bonuses, perfect-block stamina (currently 0).
- Bonemass power interaction (see 3.2 — acceptable as is).
- Movement speed penalties (reserved as a future nerf lever if towers become
  stat-sticks).

## 2. Two-handed melee adjustments

- **Balanced hyper armor:** Greatswords, battleaxes, and sledges cannot enter
  the stagger animation from the point their real attack animation starts until
  that swing's hit event finishes. This matches Goo's Combat Overhaul
  `Balanced` timing, but deliberately grants **no damage or knockback
  reduction**. Multi-target swings remain protected through their complete
  single hit check; recovery remains vulnerable. Tooltips show orange
  `Hyper-armor`.
- **Greatswords:** Primary-chain swings apply **1.5x stagger**. The secondary
  is unchanged.
- **Damage:** Greatswords, battleaxes, and sledges deal **+10% damage**
  (bonus rounded down per damage type). Atgeirs receive no hyper armor and no
  damage bonus because their spin already provides exceptional crowd control
  and stagger.
- **Slam adrenaline:** Two-handed club ground slams (`DoAreaAttack`) grant
  adrenaline **per enemy hit** (each scaled by that enemy's
  `m_enemyAdrenalineMultiplier`), matching swing attacks. Vanilla area attacks
  pay `m_attackAdrenaline x` the *highest* multiplier once per slam; the mod
  adds the difference after the slam resolves.
- Dual-wield weapons, pickaxes, and magic weapons are out of scope.

## 3. Mechanics reference (from current game build)

### 3.1 Damage/stagger pipeline order (`Character.RPC_Damage`)

1. `BlockAttack` (Humanoid): shield's own resistances (e.g. serpent scale pierce
   x0.5) -> block armor -> leftover. Stamina drains `25 x absorbed/blockPower`
   (parries use `m_perfectBlockStaminaDrain`, currently 0). Leftover is added to
   the stagger bar **here** (pre-resistance). If the bar fills, block is cancelled
   and the full raw hit proceeds.
2. Character resistances (`ApplyResistance` — Bonemass, armor mods, status
   effects).
3. Worn armor (`ApplyArmor`).
4. Final damage applied; final damage is added to the stagger bar **again**.

Consequences:

- Blocked hits fill the bar twice (shield leftover + post-armor damage).
- **Bonemass** (character resist, applies at step 2) reduces HP damage only —
  never shield stagger or stamina. Tower + Bonemass does not extend hold duration;
  it makes failures survivable. No infinite-tank risk.
- **Shield resists** (step 1) reduce stagger and stamina quadratically (x0.5 input
  -> x0.25 leftover in the quadratic regime). Serpent scale is the precedent; a
  future lever if towers should specifically counter ranged chip.

### 3.2 Key constants

- Armor formula: `damage - armor` if `damage >= 2 x armor`, else `damage^2 / (4 x armor)`.
- Stagger bar: 40% of max HP; drains 20% of the bar per second, continuously, no
  delay (`UpdateStagger`, no timer since Hearth & Home).
- Stagger while blocking cancels the block only if the **shield leftover** fills
  the bar; if the post-armor damage fills it, the player staggers but the block
  held.
- Blocking skill: +0.5% block armor per level. Balance target: **skill 50-75**
  (natural range for players who block regularly; 100 is unrealistic).
- Stamina: base 75, regen 5-10/s after 1 s delay. Player base HP 25.
- Damage modifier tiers: SlightlyResistant x0.75, Resistant x0.5, VeryResistant
  x0.25 (SlightlyResistant/SlightlyWeak exist in the current build).
- Difficulty: Hard = 150% enemy damage, Very Hard = 200%.

### 3.3 Balance findings (sandbox, Charred Warrior scenarios)

- Vanilla, best tank loadout: staggered by the 2nd blocked light hit under
  two-warrior pressure on Hard; instantly on Very Hard at low skill. Death always
  arrives with most stamina unspent — stagger is the sole limiting factor, which
  is the core problem this rework fixes.
- With +70 grant / +5% tower armor / skill 60 / 2HP+1stam food, two Hard warriors:
  first stagger and stamina empty land near the same window — accepted sweet spot.
  Three+ warriors break the hold (accepted as swarm pressure).
- Very Hard archer arrows (400 raw) leave ~190 through a Flametal tower — an
  instant block-break. Accepted: archers are the hard counter, reposition or dodge.
- 1HP+2stam is the pure-holding optimum at the cost of a thin HP margin; 3-stam
  self-destructs. Acceptable spread, revisit if 1+2 dominates.
- Parry max-hit (skill 60, 2HP+1stam, empty bar): Carapace buckler +20 ~351 safe;
  Flametal round +45 ~348 safe. Buckler remains better per-parry mitigation.
  Tower single-hit ceiling can exceed both while holding — fine, towers cannot
  parry (no enemy interrupt).

## 4. Implementation seams

- Stagger grant: Harmony postfix on `Character.GetStaggerTreshold` (or
  `Player`-scoped override) adding the equipped shield's grant. HUD bar scales
  automatically via `GetStaggerPercentage`.
- On shield equip/unequip (including R hide/draw): **preserve stagger %** —
  `m_staggerDamage = oldPercent × newThreshold`. Stops the unequip→reequip
  exploit of dumping absolute fill onto a smaller bar. (Previously only clamped
  fill down to the new max.)
- Shield identification: `ItemDrop.ItemData.m_shared` — `m_timedBlockBonus`
  distinguishes buckler (2.5x) / round (1.5x) / tower (1x); block power via
  `GetBlockPower(skillFactor)`.
- Grant/armor/durability values: config table keyed by prefab name, seeded by the
  formulas above.
- Tooltip: patch the item tooltip builder (`ItemDrop.ItemData.GetTooltip`) to
  append the stagger line with the stagger-bar color.
- Durability: adjust `m_shared.m_maxDurability` (and per-quality durability gain)
  for tower/round shields.
- Block armor: apply +5% (round up) to tower shield shared block armor values.

## 4.1 Multiplayer config

- Server-authoritative sync (`General.SyncConfigInMultiplayer`, default on): host /
  dedicated server pushes effective settings to clients on join and when the host
  edits config. Clients read synced values at runtime without overwriting their
  local `.cfg`. Mod version must match; mismatch falls back to local config with
  a warning.

## 5. Open questions

- ~~Does the invariant need to hold on Very Hard?~~ **Resolved (Aug 6):** there is
  no absolute invariant. Stagger must not be the sole limiting factor; which
  resource limits the hold should follow from the player's food trade-offs (see
  Design goals). Hard duo pressure with a balanced loadout is the tuning sweet
  spot; Very Hard and swarm pressure are allowed to break holds.
- ~~Round / buckler / tower stagger anchors?~~ **Resolved (Aug 15):** Flametal
  tower **+70** (down from +80), Flametal round **+45**, Carapace buckler **+20**.
  Tower lower tiers reseeded from leftover-vs-native-medium-hit (see R1).
- Exact HUD stagger bar color ~~(read from Hud prefab)~~ **Resolved:** orange;
  tooltip uses `<color=orange>` like other item stats.
- Per-tier grant values for pre-Ashlands shields (seed formula vs hand tuning).
- Whether 2H weapon blocking gets its own treatment (deferred; baseline test:
  2H axe vs Mountain/Plains creatures).
