# Audible Horn

A Valheim mod that adds a craftable horn. Sounding it lets other players nearby hear it, so people can find each other on a no-map server.

## Language

**Signal Horn**:
The craftable item this mod adds. Looks like Odin's drinking horn but is a separate item; vanilla tankards are untouched. Crafted at a workbench, no upgrade needed, from bone fragments, leather scraps and resin. Equipped in the hand like a tankard and sounded with the attack input. Can be sounded anywhere the player can hold it: standing, seated at a rudder or bench, swimming, or riding. The drinking animation plays only when the body is free; the Horn Call is made regardless. Has no durability and costs no stamina to sound.
_Avoid_: Horn (ambiguous with Odin's Tankard and the anniversary tankard), Tankard

**Horn Call**:
One sounding of a Signal Horn by a player. Every Listener hears the same sound; calls are not distinguishable by who made them. Hearing it is the only effect: no text, no map marker, no monster reaction. Lasts a few seconds and travels with the Blower for its duration, so a Listener hears a moving boat, not its wake.

**Blower**:
The player who makes a Horn Call. Always hears their own call.

**Listener**:
Any player, including the Blower, within Hearing Range of a Horn Call at the moment it is made. A client without the mod is never a Listener.

**Hearing Range**:
The straight-line distance from the Blower beyond which a Horn Call is silent. Terrain and buildings do not block or muffle it. Set by the host and always followed by every client, default 100 m. Loudness falls off linearly from full at the Blower to silence at the edge, so a Listener can judge rough distance.
_Avoid_: Range (alone), radius

**Horn Cooldown**:
The minimum time one player must wait between their own Horn Calls. Set by the host and always followed by every client, default 10 seconds. A Blower who tries early sees a centre-screen message and nothing is heard.

**Horn Volume**:
A per-player loudness multiplier for Horn Calls, on top of the game's effects volume. Adjusted with a slider in the game's own audio settings screen. Never shared with other players.
