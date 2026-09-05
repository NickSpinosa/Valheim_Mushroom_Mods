# Audible Horn — design notes

Things learned while building this mod that the code alone does not explain. The
vocabulary is in [CONTEXT.md](CONTEXT.md); the build plan is in
[tickets/](tickets/).

## Which reference-root property the csproj uses

Ticket 01 says to copy `haldor-expansion/HaldorExpansion.csproj`, but haldor
resolves the game through a `Local.props` import and errors out if `ValheimDir`
is unset. That fails the ticket's own acceptance criterion — a bare
`dotnet build` with no arguments — because `Local.props` is gitignored and does
not exist in a fresh checkout.

So the reference block here is MushroomSync's instead: `ValheimDir` defaults from
the Steam registry (app 892970), then `ValheimManaged` and `BepInExCore` default
from it, each guarded by `Condition="'$(X)' == ''"`. CI is unaffected either way
— `docs/devops.md` says the composite action passes *every* spelling of the
reference root as an MSBuild global property, and global properties beat anything
a project sets, so the `Condition` blocks simply never fire on a runner.

The practical rule: a new mod here should take its reference block from
MushroomSync, not from haldor.

## Why sync has no opt-out gate

`Plugin.Awake` calls neither `ConfigSync.GatedBy` nor `AcceptedWhen`, unlike
Combat Adjustments and Haldor Expansion. That is deliberate, not an oversight.

Hearing Range and Horn Cooldown are not preferences — they are a shared fiction
between two machines. If a Listener's Hearing Range is larger than the Blower's,
they hear a Horn Call the Blower's client believes was out of earshot; if a
client keeps a shorter Cooldown than the server, it sounds the horn more often
than the server allows. Either way the two players disagree about what happened,
which is the one failure this mod cannot tolerate, because hearing the call *is*
the whole feature.

Horn Volume is the opposite case and is `Exclude`d: it is a personal loudness
multiplier, it changes nothing another player observes, and a host overwriting it
would be an intrusion.

## Why not ZSFX

`ZSFX` is the game's own sound component, and reaching for it is the obvious move:
it already handles concurrency limits, reverb by distance, randomised pitch and
volume, and fade-out. It was rejected, and it should not be retried.

ZSFX is built around *prefabs*, not around runtime clips. Its clips come from a
`m_audioClips` array populated in the editor, and its instances are expected to be
spawned from a prefab that `ZNetScene` knows about — the component's whole
lifecycle assumes a registered prefab and the hash registry that goes with it. A
Horn Call has neither: the clip is decoded out of this DLL's own resources at
runtime, and the sound is a transient one-shot with no networked object behind it.
Using ZSFX would mean fabricating a prefab at load, registering it, and then
overwriting `m_audioClips` on each instance — a lot of machinery to end up with
the same `AudioSource` that `HornAudio.Play` creates in nine lines.

What ZSFX offers over a plain `AudioSource` is also mostly not wanted here. Its
concurrency cap and randomised pitch are right for a hundred overlapping combat
sounds and wrong for a signal: a Horn Call is rare (there is a Horn Cooldown), and
it must sound the same every time, because a Listener judges distance from its
loudness. Randomising that would defeat the feature.

The one thing ZSFX is still needed for is finding the mixer group — see below.

## Finding the SFX mixer group

Horn Calls must follow the game's own Effects slider, which means routing the
`AudioSource` through the mixer group vanilla sound effects use. There is no
public handle on it. `AudioMan` exposes `m_masterMixer` (the `AudioMixer` itself),
`m_ambientMixer` and `m_guiMixer` (both `AudioMixerGroup`) — everything except the
one that is wanted. `AudioMan.GetSFXVolume()` / `SetSFXVolume(float)` are public
and static, and the mixer's exposed parameter is named `SfxVol`, but a parameter
value is not a group and cannot be assigned to `outputAudioMixerGroup`.

Two approaches were considered:

- **Read `SfxVol` and fold it into the source's `volume`.** Rejected: it
  duplicates the mixer's own maths, it needs re-reading whenever the player moves
  the slider mid-call, and it silently diverges the moment the game changes how
  that parameter maps to gain.
- **Borrow the group from something already routed to it.** Taken. Every vanilla
  `ZSFX` prefab carries an `AudioSource` whose `outputAudioMixerGroup` *is* the SFX
  group, so `HornAudio` walks `ZNetScene.instance.m_prefabs` on the first Horn
  Call, takes the first prefab with a `ZSFX` whose `AudioSource` has a non-null
  group, and caches it.

The scan is lazy because `ZNetScene.instance` does not exist at plugin load, and
it settles permanently once `ZNetScene` is up: the prefab list does not change
after `Awake`, so a full scan that found nothing will not find anything later
either, and re-walking a few thousand prefabs per Horn Call would be a waste.
Before `ZNetScene` exists the scan stays unsettled and is retried on the next
call.

**Group name observed: to be filled in from the first in-game run.** The mod logs
it once, at Info, as `SFX mixer group '<name>' taken from prefab '<prefab>'`.

A missing group is a degradation, not a failure. The call still plays; it just
sits outside the Effects slider, and a Warning says so once. This is why
`HornAudio.IsReady` deliberately does **not** include the group in its answer,
despite ticket 03's inline comment saying "clip loaded and mixer group found":
ticket 04 gates playback on `IsReady`, so folding the group into it would turn a
cosmetic fallback into total silence — the opposite of what the same ticket
specifies two paragraphs earlier. `IsReady` means "this process has an
`AudioListener` and the clip decoded", which is the question ticket 04 is actually
asking.

## Referencing UnityEngine.AudioModule from net472 costs two workarounds

Audible Horn is the first net472 project in this repo to use types out of
`UnityEngine.AudioModule`. Merely referencing the assembly is fine — the scaffold
did that from ticket 01 and nothing complained. Touching a type inside it is not,
and the two failures that follow look unrelated but are the same root cause.

1. **CS1705.** `UnityEngine.AudioModule` is built against netstandard **2.1**;
   a net472 target supplies the 2.0 facade, and the compiler refuses the moment it
   has to load a type from the assembly. The fix is a `<Reference
   Include="netstandard">` pointing at the game's own
   `valheim_Data/Managed/netstandard.dll`. Retargeting is not an option in either
   direction: netstandard2.1 cannot reference MushroomSync's net472, and net48 is
   still capped at netstandard 2.0.

2. **CS0518 on `AudioClip.SetData`**, caused by the fix for the first. With
   netstandard 2.1 in the compilation the compiler now sees Unity's
   `SetData(ReadOnlySpan<float>, int)` overload, and binding the call requires
   resolving `ReadOnlySpan<T>` — which net472 does not define and Unity's
   netstandard 2.1 only *forwards* to a Mono `mscorlib` this project does not
   reference. The array overload is an exact match and is still unreachable,
   because overload resolution has to type every candidate first. `WavLoader`
   therefore picks `SetData(float[], int)` by signature through reflection, once
   per process.

The general shape to expect: any Unity API with both an array and a
`Span`/`ReadOnlySpan` overload is unbindable from this project. Reach for
reflection at that one call site rather than restructuring the reference set —
pulling in the game's whole Mono BCL with `NoStdLib` was the alternative, and it
would make this mod the only one here that does not build like the others.

## net472 is forced, not chosen

`MushroomSync` targets `net472` and cannot be netstandard (its csproj carries the
reasoning: the game's UnityEngine assemblies are built against netstandard 2.1,
so a 2.0 target fails with CS1705, and a 2.1 target could not be referenced by the
net47x mods). Anything that references MushroomSync inherits that constraint.
vegvisir-compass is the mod that does not, and it is the mod that cannot use
sync.
