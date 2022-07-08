# Dronemeld Changelog

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