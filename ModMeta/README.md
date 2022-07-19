
# Dronemeld

## SUPPORT DISCLAIMER

### Use of a mod manager is STRONGLY RECOMMENDED.

Seriously, use a mod manager.

If the versions of Dronemeld or TILER2 (or possibly any other mods) are different between your game and other players' in multiplayer, things WILL break. If TILER2 is causing kicks for "unspecified reason", it's likely due to a mod version mismatch. Ensure that all players in a server, including the host and/or dedicated server, are using the same mod versions before reporting a bug.

**While reporting a bug, make sure to post a console log** (`path/to/RoR2/BepInEx/LogOutput.log`) from a run of the game where the bug happened; this often provides important information about why the bug is happening. If the bug is multiplayer-only, please try to include logs from both server and client.

## Description

Whenever a duplicate drone, turret, or other AI ally of any one type is obtained, Dronemeld will prevent it from spawning. Instead, the existing ally of that type will gain significant boosts to base health, attack speed, attack damage, and cooldown reduction. This cuts down on some lag and a lot of visual/audial clutter in long, drone-heavy games.

Includes config options for (default value):

- How many duplicate allies to allow (1).
- Whether to count all allies per team, or individually per player (per player).
- What order to run upgrades in if more than 1 ally per type is allowed, between round-robin, first-only, or random (round-robin).
- Which allies to affect (Gunner, Missile, Flame, Healing, TC-280 Prototype, Gunner Turret, The Back-up, Squid Polyp, Queen's Gland, Defense Nucleus, Newly-Hatched Zoea).
- Which special-case allies to affect:
	- Goobo Jr. (enabled).
- Quantum Turrets: automatically teleport turrets and other immobile allies to the nearest remembered purchase position to the owner (enabled).
	- Which turrets to apply this behavior to (Gunner Turret, Squid Polyp, Defense Nucleus).
- Individual stat boosts per stack for health, damage, attack speed, and CDR (+1x, +0.6x, +0.6x, +0.6x).
- Whether, and how strongly, to display a clientside model size increase per stack (+0.25x).

## Issues/TODO

- Does not yet function on Equipment Drones.
- See the GitHub repo for more!

## Changelog

The 5 latest updates are listed below. For a full changelog, see: https://github.com/ThinkInvis/RoR2-Dronemeld/blob/master/changelog.md

**1.2.0**

- Now works on Squid Polyp, Queen's Gland, Defense Nucleus, and Newly-Hatched Zoea.
- Fixed Empathy Cores not counting Dronemeld stacks as additional allies.
- Now provides Quantum Turrets behavior for Squid Polyp and Defense Nucleus by default.

**1.1.0**

- Now works on The Back-up and Goobo Jr.
- Added a Quantum Turrets feature which continuously teleports turrets to the closest of all turret purchase points to their owner.
- Fixed dead drones not preserving Dronemeld stacks. Dying drones will now spawn multiple purchasables on impact, depending on the amount of Dronemeld stacks the drone had.
- Removed the "all-at-once" upgrade order option, due to conflicting with preservation of Dronemeld stacks.
- VfxResize client config option is now deferred until stage end.
- Fixed slightly incorrect application of VfxResize option (will no longer offset Goobo Jr. into the ground instead of resizing, and will not also resize the HUD arrow).
- Changed ally card stack count text from a hardcoded color to the "cStack" style.

**1.0.0**

- Initial version.
	- Causes duplicate drone purchases to increase existing drones' stats instead of spawning a new one.
	- Optionally also increases model size.
	- Includes config for quantity before counting as duplicates, and upgrade order if this is > 1.
	- Includes option for counting all drones per team instead of per player.