# Point-shop UI material compatibility regression (1.7)

Issue #1 reports an asynchronous `ImGuiMaterialRenderer.CreatePipeline` / `vkCreateGraphicsPipelines` failure on Radeon RX 560X with standalone RimUnity 0.1.17. The report does not identify the failed shader. This probe verifies that the shop avoids the custom UI material path for both the native Nira portrait and merchandise.

Run only against an isolated complete game copy at `ValidationRoot/Game`, with matching Harmony, Alien Races, Nivarian Race and MotherShip copies in its Mods folder. Requires PowerShell 7 and the .NET 10 SDK. The runner creates fresh savedata and archives the actual Waylith log, input hashes, screenshots and owned process identity.

```powershell
./Tests/PointShopCompat17/Run.ps1 -ValidationRoot D:/Validation/ShopCompat17 -Label Baseline-R1
./Tests/PointShopCompat17/Run.ps1 -ValidationRoot D:/Validation/ShopCompat17 -Label Fixed-R1
./Tests/PointShopCompat17/Run.ps1 -ValidationRoot D:/Validation/ShopCompat17 -Label Runtime017-R1 -StandaloneRuntime
```

Install the original r9 core for the baseline, then the candidate before the fixed run. Use unique labels. For the standalone run, put the matching RimUnity mod under `Game/Mods/RimUnity`; its package ID is read from About.xml. Optional `-VulkanDriverManifest` selects a locally installed Vulkan driver.

The probe opens through the real main-button action three times, repaints, switches donation/buy pages, scrolls and closes. During the middle cycle it injects an optional material into native merchandise-icon resolution. A test-only guard counts and rejects custom-material submissions at the Graphics/GenUI boundary, preventing deliberately injected materials from reaching the driver. On the fixed build the guard rejects zero calls, while normal texture drawing executes. This is a regression invariant, not a reproduction of the AMD driver crash. The production core contains no guard or Harmony patch from this test.

The original r9 build fails seven assertions (portrait materials, custom submissions and lost plain drawing under injection). The fixed core passes 29 checks each with embedded RimUnity 0.1.16 and standalone 0.1.17, with screenshots inspected. Both runs used NVIDIA RTX 2080 Ti; the reporter's RX 560X, actual save and full modpack remain untested. The upstream AncientHeavyBridge missing-def error occurs on both baseline and fixed runs.
