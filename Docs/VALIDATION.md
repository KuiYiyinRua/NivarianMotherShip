# Release validation: 0.9.6

The release includes five languages. There are 478 runtime text keys, two metadata keys and 293 definition fields. Without Royalty, the four native psycast fields are omitted and 289 fields load.

## Checks

- Core and optional Multiplayer source compile in Release configuration with zero warnings and zero errors.
- Catalog and XML checks cover all five languages, source key references, duplicate keys, positional format arguments, rich-text tags and game grammar tokens.
- Regenerating translation XML is repeatable and passes the same checks.
- RimWorld 1.6.4850 rev646 loads the actual release assemblies in isolated save-data directories.
- The native language loader is checked for ChineseSimplified, English, Russian, Japanese and French. Every expected Keyed value is compared to the loaded dictionary and active Translate result. Every expected DefInjected value is compared to the actual definition field after native injection.
- Both Royalty and NoRoyalty branches are checked. The Royalty configuration includes optional Milira; the NoRoyalty configuration omits it.
- No NMC translation injection errors occur in the successful runs. Unrelated base-mod translation warnings are excluded from that claim.
- Assets, gameplay definitions, costs, prerequisites and load conditions match the 0.9.6 development baseline. The orbital strike description is corrected to say that friendly-fire protection requires research.
- Multiplayer adapter C# code is unchanged from the 0.9.6 development baseline. Text changes retain invariant save keys, definitions and synchronized pawn identities.
- Release ZIP entries and SHA-256 digests are checked against packaged files. Local deployment and downloaded GitHub assets are verified against the same package manifest.

## Limits

These checks establish localization coverage, native loading, build and package integrity. They do not establish exhaustive visual layout coverage, gameplay regression results for the new 0.9.6 research migration, or new Multiplayer dual-client/long-running tests of the 0.9.5 battery upgrade and 0.9.6 research change.

The first NoRoyalty configuration included optional Milira content and encountered external recipe/permit definition errors; RimWorld recovered to Core-only loading. That run was rejected. The successful NoRoyalty configuration uses the required base mods without Milira. The expansion does not remove dependency errors in other mods.

Game and dependency assemblies, player saves, raw local test harnesses and development backups are not included in the release ZIP or repository.
