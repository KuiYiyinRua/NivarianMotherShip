# Nivarian Mother Ship Expanded

[简体中文](Docs/README.zh-CN.md) · English · [Русский](Docs/README.ru.md) · [日本語](Docs/README.ja.md) · [Français](Docs/README.fr.md)

A RimWorld 1.6 expansion for **Nivarian Race**. Extend the mothership with 19 tactical supports, a points shop, carrier and gravity ship, and an eight-stage Continuum campaign.

Version: **0.9.6**. Languages: Simplified Chinese, English, Russian, Japanese and French.

## Installation

1. Download the mod ZIP from [Releases](https://github.com/KuiYiyinRua/NivarianMotherShip/releases).
2. Extract the NivarianMotherShip folder into your RimWorld Mods folder. Replace the previous copy instead of installing two copies.
3. Enable Harmony, Humanoid Alien Races, Nivarian Race and this expansion in that order. Follow any dependency requirements of the installed base mods.
4. Select your language in RimWorld settings and let the game reload its data. This expansion uses the game's language selection.

Required: [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077), [Humanoid Alien Races](https://steamcommunity.com/sharedfiles/filedetails/?id=839005762) and [Nivarian Race](https://steamcommunity.com/sharedfiles/filedetails/?id=3624805128). They are not bundled.

Royalty enables native psycast integration; the expansion includes fallback abilities when Royalty is absent. Multiplayer support loads when rwmt.Multiplayer is active. Optional Milira content supplies starship batteries. The Continuum campaign requires keeptpa.NivarianDraconicMilitary; without that addon, support research is available directly.

## Play

Use the Nivarian mothership support window to open the commander page, and the uplink research terminal to unlock support protocols. The commander HUD provides access to selected supports. Costs, prerequisites and targeting restrictions are shown in the game.

The carrier and gravity ship transport people and cargo. With Milira enabled, request a starship battery upgrade and have colonists deliver the required batteries: ten for the carrier and two for the gravity ship. Once installed, fuel is spent on takeoff rather than continuous flight.

In 0.9.6, **precision ally recognition** is a separate research project after orbital strike support. It requires 3,000 research, 250 steel, eight industrial components and 30 dragon crystals. Completing it permanently enables friendly-fire protection. An existing purchased unlock remains effective and is migrated automatically.

## Development

The repository includes compiled mod assemblies, C# source and localization resources. Game and dependency assemblies must be supplied locally.

~~~powershell
dotnet build Source/NivarianCommander.csproj -c Release -p:GameRoot="D:/Games/RimWorld"
dotnet build Multiplayer/Source/NivarianCommander.Multiplayer.csproj -c Release -p:GameRoot="D:/Games/RimWorld"
python Tools/Localization/verify.py
~~~

Directory.Build.props supplies default dependency paths. Override HarmonyAssembly, NivarianRaceAssembly, LumiParticleAssembly or MultiplayerApiAssembly if your installation uses different folders. Builds write directly to the mod's assembly folders. Python tools use only the standard library.

Translation contributors: see [localization maintenance](Docs/LOCALIZATION.md). Release verification and its limits are recorded in [release validation](Docs/VALIDATION.md).

Nivarian Race is created by **keeptpa**. This expansion depends on the original mod and does not include its assemblies.
