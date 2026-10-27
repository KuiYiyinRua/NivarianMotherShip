# TD overlay transparency regression (RimWorld 1.7)

Waylith gives mod shader roots priority and falls back to matching a source's
basename recursively. A subdirectory alone does not isolate a shader named
`VertexColor.frag.glsl` from Core's shader of the same name. The NMC effect
fragment uses vertex alpha alone; Core's fragment also multiplies `_Color`,
which `CellBoolDrawer` uses for overlay opacity.

`VerifyShaderNames.ps1 -GameRoot <1.7 installation>` checks actual Core source
names for collisions and checks the mod's XML source paths. Keep the effect
source names unique; do not compensate by changing TD's opacity settings.

`Probe.csproj` builds a separate opt-in test mod (`local.nmc.tdoverlay17probe`).
It requires the exact game/Harmony assemblies, the production MotherShip mod,
matching Nivarian/HAR/RimUnity dependencies, and TD Enhancement Pack Continued
(`MemeGoddess.TDPack`). No probe belongs in a production Assemblies folder.

Prepare an isolated `ValidationRoot/Game` installation and place the probe in
`Mods/Probe/Assemblies`, with its package metadata in `Mods/Probe/About`.
`Run.ps1 -ValidationRoot <path> -Label <unique label>` creates a separate save
folder, freezes input hashes, uses `-quicktest -nmc-td-overlay17`, and captures
the actual log, process identity, evidence, and five in-game screenshots.

The probe checks actual shader resolution, `_Color` availability and alpha,
three NMC shader registrations, real TD fertility drawers at two opacities,
and trade-beacon coverage geometry/materials. It also draws a sample using
the production NMC vertex-alpha shader. Review logs and screenshots after
the run; the controller's `COMPLETED` marker alone does not mean assertions
passed. The pre-fix asset must fail the Core-resolution and opacity checks;
the fixed asset must pass, and lowering TD opacity must visibly fade its
overlay. Keep game build and RimUnity version paired in both runs.

Validation evidence: `BuildValidation/TDOverlay17-20261027/`.
