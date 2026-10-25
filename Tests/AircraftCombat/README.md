# Aircraft combat regression

`Run.ps1` requires a prepared isolated RimWorld 1.7 `ValidationRoot/Game` containing Harmony, AlienRaces, NivarianRace and NivarianMotherShip, plus the matching core reference at `ValidationRoot/Build17/Bin/NivarianCommander.dll`. The probe is a deferred `.rimunity` assembly and activates only with `-nmc-aircraft-combat`.

The runner uses separate savedata, input hashes, a unique label, silent background rendering and a bounded timeout. It archives PID-matched game logs. It never changes player saves or configurations. Use a fresh label for every run and review raw logs after `PROBE_PASS`.

For 1.6, build `Probe16.csproj` with `CommanderAssembly` pointing to the candidate core, then use `Run16.ps1 -Label <unique-label>`. It reads the 1.6 game two directories above this mod and copies dependencies into a fresh temporary test installation. The candidate core is taken from `BuildValidation/AircraftCombat-20261025/Build16/`; `ValidationFolder` can select a different validation root. It shares the same functional assertions with the 1.7 probe.

The probe pauses the isolated world and drives actual ship, interceptor and movement methods. It verifies projectile intended-target references, then removes those projectiles to keep test targets intact; damage and projectile collision are not covered. It executes three target-selection rounds, recalls and recovers aircraft, checks both sides of range limits, and checks a real storm projectile.

Navigation tests exercise five ship definitions, deterministic route choice, entire-hull roof costs, a full-map mountain barrier that must remain traversable, a narrow passage, player movement, changed terrain costs, and AI station selection. The final same-process save/load compares exact target references, hangar/deployed membership and persisted route progress. The `LoadedGame` callback pauses before the first simulation update.

These are isolated single-player checks, not a multiplayer soak or a full player-save certification.
