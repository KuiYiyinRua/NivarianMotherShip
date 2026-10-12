# Changelog

## 0.9.8

- Retuned Spacetime Wave: friendly pawns and animals move at 2× speed, friendly work runs at 10× speed, and friendly turret burst cooldowns become one third. Nivarian research and manufacturing run at 10× speed, including carrier interceptor manufacturing; P/V income remains unchanged.
- Added persistent nutrient target sliders and automatic-refill switches for nutrient and shadow energy towers, with synchronized Multiplayer commands.
- Updated orbital and solar bombardment to a shared 3×3 impact, wider beams and revised impact visuals. Expanded void stasis to radius 18 and allowed overlapping placement.
- Continuum missions persist until resolved; the first offer arrives after seven days, with broader valid settlement and caravan targeting. Added the updated world-map base icon.
- Carrier and gravity-ship destruction now returns 50% of their recipe materials. Optimized projection and power lookup paths while preserving saved state.
- Includes all current expansion source, compiled assemblies, five-language localization and Workshop materials. Added the GitHub source link to the bottom of every Workshop description.
- Validation: focused two-client/two-map Spacetime checks reached 10,006 shared ticks with three casts and matching state/Rand receipts before the user stopped testing. The subsequent carrier manufacturing fix and 0.9.8 build are compiled and statically inspected; no additional game test, full mod-pack soak or cold rejoin is claimed.

## 0.9.7

- Added a default-off setting to complete all expansion research without Continuum missions and bypass support level requirements. Costs and cooldowns remain active; completed research is retained when the option is disabled. The saved toggle is synchronized in Multiplayer.
- Updated the author credit to 尹怨怨 and prepared five-language Steam Workshop descriptions with actual in-game screenshots.
- Includes current carrier, warship research, hostile campaign reinforcement and performance updates.

## 0.9.6

- Continuum stages 6/7/8 add respectively 2/2/4 hostile gravity ships and 0/1/2 hostile carriers, with one real pilot per hull. Gravity ships automatically pursue and orbit targets; carriers hover near their launch range and manufacture interceptors from 500 dragonfrost steel and 8,000 chemfuel each. Reinforcement and flight state persist through saves.
- Automatic support targeting skips downed pawns, and fleet weapons release targets that become downed.
- Purification beam visuals now use the actual four-cell damage radius for the impact footprint and wider column.
- Added Simplified Chinese, English, Russian, Japanese and French localization for commander UI, supports, research, shop dialogue, equipment, ships, campaign and messages.
- Precision ally recognition now unlocks through a separate research project. The old purchased protection is retained and migrated automatically.
- Includes the 0.9.5 Milira starship battery upgrades: ten batteries for the carrier, two for the gravity ship, then fuel is consumed on takeoff.
- Keeps Royalty and NoRoyalty loading branches and the optional Multiplayer adapter.
- Fixed translation file precedence between Royalty and the base ability definitions.
