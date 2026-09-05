# 01 — Project scaffold and config

**Goal:** a new mod directory that compiles to `AudibleHorn.dll`, loads in
BepInEx, depends on MushroomSync, and exposes the three settings from the
glossary. No gameplay yet.

**Depends on:** nothing. **Unblocks:** everything else.

## Deliverables

```
audible-horn/
├── AudibleHorn.csproj
├── README.md              (stub: title, one-line description, "see docs/")
├── docs/CONTEXT.md        (exists — do not edit)
├── docs/tickets/          (exists)
└── src/
    ├── Plugin.cs
    └── ModConfig.cs
```

### `AudibleHorn.csproj`

Copy `haldor-expansion/HaldorExpansion.csproj` and change:

- `AssemblyName` and `RootNamespace` → `AudibleHorn`, `Version` → `0.1.0`.
- Replace the `Local.props` import and the `CheckValheimDir` error with the
  Steam-registry lookup used by `MushroomSync/MushroomSync.csproj` (the
  `ValheimDir` → `ValheimManaged` / `BepInExCore` property block), so a bare
  `dotnet build` works locally and CI's `ValheimDir` global property still
  overrides it. Keep an `<Error>` on `!Exists('$(ValheimManaged)\assembly_valheim.dll')`.
- Keep `<ProjectReference Include="..\MushroomSync\MushroomSync.csproj" Private="false" />`
  and the comment explaining why `Private=false`.
- References, all `Private=false` with `HintPath` under the managed dir:
  `assembly_valheim`, `assembly_utils`, `assembly_guiutils`, `UnityEngine`,
  `UnityEngine.CoreModule`, `UnityEngine.AudioModule`, `UnityEngine.UI`,
  `Unity.TextMeshPro`, and under BepInEx core: `BepInEx`, `0Harmony`. All of
  these are already in the CI verify list in
  `.github/actions/build-mods/action.yml`, so no workflow change is needed.
- `TargetFramework` stays `net472`. MushroomSync cannot be referenced from
  netstandard2.1; the comment in its csproj explains why.
- Add `<ItemGroup><EmbeddedResource Include="Assets\**\*.wav" /></ItemGroup>`
  now so ticket 03 only has to drop a file in. `Assets/` may be absent at this
  point; MSBuild tolerates an empty glob.
- Keep the `CopyToPluginsFolder` target so `-p:CopyToPlugins=true` installs
  the DLL into the local game for testing.

### `src/Plugin.cs`

```csharp
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(MushroomSync.MushroomSyncPlugin.PluginGuid)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "mushroom.audiblehorn";
    public const string PluginName = "Audible Horn";
    public const string PluginVersion = "0.1.0";
    internal static ManualLogSource Log;
    internal static ModConfig Settings;
    internal static ConfigSync Sync;
    ...
}
```

`Awake` order matters and mirrors `haldor-expansion/src/Plugin.cs`:

1. `Log = Logger;`
2. `Sync = ConfigSync.Create(PluginGuid, PluginVersion, Logger).Protecting(Config);`
3. `Settings = new ModConfig(Config);` — binds entries and registers them (below).
4. `Sync.WatchForChanges(Config).Start();` — **no `GatedBy`, no `AcceptedWhen`.**
   The design decision is that the host always publishes and clients always
   follow, because two players disagreeing on Hearing Range means one hears a
   call the other did not send. Put that reasoning in a comment where a future
   reader would otherwise add a gate.
5. `new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);` keep the
   instance and call `UnpatchSelf()` in `OnDestroy`.
6. `Log.LogInfo(PluginName + " " + PluginVersion + " loaded.");`

### `src/ModConfig.cs`

Bind exactly these entries. Section and key names are public surface (players
edit the `.cfg`), so use them verbatim.

| Section | Key | Type | Default | Range | Synced | Description |
|---|---|---|---|---|---|---|
| `Horn` | `HearingRange` | float | `100` | 10–2000 | yes | Metres from the Blower beyond which a Horn Call is silent. Straight-line; terrain does not block it. Set by the host. |
| `Horn` | `Cooldown` | float | `10` | 0–120 | yes | Seconds a player must wait between their own Horn Calls. Set by the host. |
| `Audio` | `HornVolume` | float | `1.0` | 0–1 | **no** | Personal loudness multiplier for Horn Calls, on top of the game's effects volume. Never shared with other players. |

Use `AcceptableValueRange<float>`. Register the first two with
`Plugin.Sync.Register(...)`; call `Plugin.Sync.Exclude(HornVolume)` for the
third. Expose them as `ConfigEntry<float>` properties named `HearingRange`,
`Cooldown`, `HornVolume`.

From `MushroomSync/README.md`: after registration `entry.Value` already returns
the host's value on a synced client. No call site needs to know which it got.

## Acceptance

- `dotnet build audible-horn/AudibleHorn.csproj -c Release` succeeds locally
  with no arguments on a machine with Steam Valheim installed.
- The DLL loads in game with MushroomSync present. The BepInEx log shows
  `Audible Horn 0.1.0 loaded.` and no errors.
- `BepInEx/config/mushroom.audiblehorn.cfg` is generated with the three entries
  and the descriptions above.
- Joining a server whose `HearingRange` differs from the client's `.cfg`: the
  client log shows MushroomSync applying host values and the local `.cfg` is
  not rewritten.
- Root `README.md` and `MushroomSync/README.md` are **not** touched here; that
  is ticket 08.
