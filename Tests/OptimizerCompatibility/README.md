# Optimizer cache regression

Build `Probe.csproj`, then use `Run.ps1 -Label <unique-name>` for an isolated graphical RimWorld run. Add `-Baseline` to select the frozen original core or `-NoOptimizers` to test optional dependencies. The current archived baseline and candidate are in `BuildValidation/OptimizerBridge-20261013`.

The driver is inactive without `-nmc-optimizer-probe`. It checks the actual YaOpt adapter after three native `OnPatch` calls with all parallel simulation options disabled. It requires PO's transpiler to remain installed on `FieldWarpVisuals.MapComponentUpdate`, then performs 1,048,576 real cached queries across eight threads and 512 isolated map objects. Every returned component must match its owner. Finally it runs 600 live map-update frames.

The original core must fail the lock-enabled assertion; the candidate must pass. The controller scans the log for collection corruption and rendering errors, preserves input hashes, and only stops processes it launched with the matching isolated path.

The archived MP driver separately tests three synchronized shield/warp visual activations, joining-state equality, continued two-player play, and 120,000 shared world ticks. Synthetic concurrent cache probes are not injected into the player's save or into multiplayer simulation.
