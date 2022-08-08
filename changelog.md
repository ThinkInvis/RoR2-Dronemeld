# Dronemeld Changelog

**1.3.0**

- Increased null safety of drone death hook.
	- Fixes an issue with The Back-up.
- Added better mod compatibility support.
	- Now exposes enabled drone types via `public static bool IsDronemeldEnabledFor(string masterPrefabName)`.
	- Now exposes `public static CharacterMaster TryApply(...)` with 2 overrides. Recommend using `CharacterMaster, string` signature.
- Updated TILER2 dependency to 7.3.2.
- For developers: NuGet config is now localized (building project no longer requires end-user modification of system or directory NuGet config).

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