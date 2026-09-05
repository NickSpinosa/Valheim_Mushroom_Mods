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

## net472 is forced, not chosen

`MushroomSync` targets `net472` and cannot be netstandard (its csproj carries the
reasoning: the game's UnityEngine assemblies are built against netstandard 2.1,
so a 2.0 target fails with CS1705, and a 2.1 target could not be referenced by the
net47x mods). Anything that references MushroomSync inherits that constraint.
vegvisir-compass is the mod that does not, and it is the mod that cannot use
sync.
