# Nivarian Race: MotherShip

[简体中文](Docs/README.zh-CN.md) · English · [Русский](Docs/README.ru.md) · [日本語](Docs/README.ja.md) · [Français](Docs/README.fr.md)

A RimWorld 1.6 / 1.7 expansion for **Nivarian Race**. Extend the mothership with 19 tactical supports, a points shop, carrier, gravity ship, Storm Ship, Sage Ship and Warp Prism, and an eight-stage Continuum campaign.

Current release: **0.9.9-r12** (core version 0.9.9). Languages: Simplified Chinese, English, Russian, Japanese and French.

## Installation

1. Subscribe on [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3817608894), or download the complete mod ZIP from [GitHub Releases](https://github.com/KuiYiyinRua/NivarianMotherShip/releases).
2. Extract the NivarianMotherShip folder into your RimWorld Mods folder. Replace the previous copy instead of installing two copies.
3. Enable Harmony, Humanoid Alien Races, Nivarian Race and this expansion in that order. Follow any dependency requirements of the installed base mods.
4. Select your language in RimWorld settings and let the game reload its data. This expansion uses the game's language selection.

Required: [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077), [Humanoid Alien Races](https://steamcommunity.com/sharedfiles/filedetails/?id=839005762) and [Nivarian Race](https://steamcommunity.com/sharedfiles/filedetails/?id=3624805128). They are not bundled.

Royalty enables native psycast integration; the expansion includes fallback abilities when Royalty is absent. The optional Multiplayer adapters load only on 1.6 with rwmt.Multiplayer; 1.7 Multiplayer is not supported by this build. Optional Milira content supplies starship batteries. On 1.6 the Continuum campaign requires keeptpa.NivarianDraconicMilitary; on 1.7 its military content is included in Nivarian Race and enables the campaign automatically.

Use 1.7 versions of the prerequisites with RimWorld 1.7. The matching RimUnity runtime is bundled with the expansion; do not copy the 1.6 DLLs into 1.7. See [1.7 migration and validation](Docs/RimWorld17.zh-CN.md).

## Play

Use the Nivarian mothership support window to open the commander page, and the uplink research terminal to unlock support protocols. The commander HUD provides access to selected supports. Costs, prerequisites and targeting restrictions are shown in the game.

The carrier and gravity ship transport people and cargo. Request a starship battery upgrade and have colonists deliver ten battery units for the carrier or two for the gravity ship. Milira starship batteries and unstable power cells each count as one unit; vanometric power cells count as two. The native building batteries must be minified first. Once installed, fuel is spent on takeoff rather than continuous flight. See the [ship upgrade guide](Docs/ShipFoldBatteryUpgrade.zh-CN.md).

In 0.9.6, **precision ally recognition** is a separate research project after orbital strike support. It requires 3,000 research, 250 steel, eight industrial components and 30 dragon crystals. Completing it permanently enables friendly-fire protection. An existing purchased unlock remains effective and is migrated automatically.

## Development

The repository includes compiled mod assemblies, C# source and localization resources. Game and dependency assemblies must be supplied locally.

~~~powershell
dotnet build Source/NivarianCommander.csproj -c Release -p:GameRoot="D:/Games/RimWorld"
dotnet build Multiplayer/Source/NivarianCommander.Multiplayer.csproj -c Release -p:GameRoot="D:/Games/RimWorld"
python Tools/Localization/verify.py
~~~

Directory.Build.props supplies default dependency paths. Override HarmonyAssembly, NivarianRaceAssembly, LumiParticleAssembly or MultiplayerApiAssembly if your installation uses different folders. Builds write directly to the mod's assembly folders. Python tools use only the standard library.

The 1.6 portion retains five optional Multiplayer adapters with 0.9.8 assembly identities. The main adapter includes caravan recall synchronization and the Continuum entry fix; the four ship adapters are unchanged. Core file/informational version is 0.9.9; its binding version remains 0.9.8. Rebuilding an adapter changes its MVID and requires revalidation with third-party patches that identify it by MVID. Additional adapter sources are in Multiplayer/ShipUpgradeSource, WarpPrismSource, StormShipSource and SageShipSource.

Translation contributors: see [localization maintenance](Docs/LOCALIZATION.md). Release verification and its limits are recorded in [release validation](Docs/VALIDATION.md).

Nivarian Race is created by **keeptpa**. This expansion depends on the original mod and does not include its assemblies.


## Research technologies without completing their missions

Off by default. Removes only this expansion’s mission requirements for research. Prerequisite technologies, material costs and normal research are still required. Does not complete technologies or missions, or bypass mothership support levels. Turning it off restores mission requirements and retains completed research.
