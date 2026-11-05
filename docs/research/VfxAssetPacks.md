# VFX asset packs — sourcing and integration (GG-T267)

Owner (2026-11-05): "need to find more asset packages for effects; free or otherwise as long as they fit into the paradigm of a premade prefab containing particle effects/sprites/etc".

## 1. What we already own (GridGame2026, archived on GitHub)
- `Assets/VisualEffects`: about 760 prefabs, e.g. ToxicCloud, ShockBolt, SleepDust, SilenceMute, ScanRays, StarSparkle, RedSlash2X, Shuriken, TechSword. Gaiden's `VfxLibrary` ImpactVfx names already mirror these, but it redraws them in code.
- `Assets/Particles`: about 83 prefabs: loot pillars (Common to Legendary), pickup cast/loop, lightning set, chest open.
- Materials from the **Vefects** and **Gamestar** packs (the Vefects SFX already ship as our sounds).
- **Before buying anything:** re-import these through the integration path below. They're licensed to the owner already.

## 2. Shortlist to add (stylized, mobile-friendly, prefab-based)
| Pack | Store | Price | Why it fits | Watch |
|---|---|---|---|---|
| Epic Toon FX | Unity Asset Store | paid | 600–930 cartoony prefabs (combat, magic, missiles, novas); one consistent toon style, which matches the cohesion goal | Big; take only the categories we need |
| Cartoon FX Remaster | Unity Asset Store | paid | 50+ polished stylized prefabs and a shared shader (UV distortion, dissolve) for a cohesive look | Smaller set |
| Combat Magic VFX Vol. 1 | Unity Asset Store | paid | 52 effects for fire, lightning, frost and hexagram magic, mapping onto our elements | Check its shader for URP |
| Unique Projectiles Vol. 1 | Unity Asset Store | paid | 30 stylized projectiles (spells, fireballs, dark magic, arrows), PC and mobile | Projectiles only |
| Toon Particle Pack SKJ Ver. 1 | Unity Asset Store | about $15 | 12 hit and 12 impact effects, marked mobile-safe | Small |
| Mobile Cartoon FX | Unity Asset Store | about $4.99 | Cheap, mobile-first, cartoon | Older |
| Brackeys VFX Bundle | itch.io | free, CC0 | Flipbooks and particles, no licence friction | Mixed style; verify contents |
| Free Unity Game VFX Collection (Ray Wang) | Gumroad | free | 10 prefab magic effects; same author sells large element mega-packs (itch.io) | Licence not stated as CC0 |
| MagusVFX 2D packs (Slashes & Impacts, Casting & Buffs) | itch.io | paid | Sprite-sheet slashes, impacts and buffs; drops straight into a UI board | No redistribution |

Prices and counts come from store listings and may be out of date; check before buying. Licences must allow commercial use in a shipped game.

## 3. The integration problem
Gaiden draws the whole board in **UI Toolkit**, with no world-space camera content. **BIBLE Law 3 / GG-LAW-9** say there are no prefabs and everything is built in code. Effect prefabs (ParticleSystem / VFX Graph) need a camera, so a bridge is required:

- **A. Live particles over UI Toolkit.** Render an effects camera to a RenderTexture shown in an overlay element, or use an adapter such as ME.UITK.Particles (tested only on Unity 6000.6 + URP 17.6). It's the most faithful, but needs a Law 3 amendment and Resources-loaded prefabs. Pause (`Tween.Paused`), battle speed and Reduce Motion must be driven into each ParticleSystem (simulationSpeed/Pause). The harness's frame captures must include the RT layer, and the extra camera costs on mobile.
- **B. Bake to flipbooks (recommended).** An Editor/batchmode tool plays each prefab and renders it frame by frame into a sprite-sheet atlas, plus a JSON with frame count, fps, pivot and size. At runtime a `FlipbookView` UI Toolkit element plays it on `Tween` time.
  - Pause, speed, Reduce Motion and the deterministic harness all just work.
  - It's cheap on mobile.
  - Law 3 holds at runtime (no prefabs in the build); only the offline tool touches prefabs.
  - The GG-T253 `vfxstyle.json` element table can point each element and tier at a flipbook.
  - It loses particles that react to the board live. Mitigate by keeping code-drawn motes and trails for that.
- **C. Sprite-sheet packs only.** Use packs like MagusVFX's directly in the flipbook player; no baking tool is needed, but there are fewer choices.

## 4. Recommendation
Option B: a baking tool, plus re-baking the roughly 840 prefabs we already own first. Fill gaps with one consistent toon family (Epic Toon FX, or Cartoon FX Remaster for a tighter look) so the effects read as one language, as POL-E3 requires. GG-T253's style table is the mapping layer.

Sources: [80.lv, 5 VFX packs for stylized Unity projects](https://80.lv/articles/5-vfx-packs-for-stylized-unity-projects) · [80.lv, best VFX for Unity](https://80.lv/articles/best-vfx-for-unity) · [Unique Projectiles Vol. 1](https://assetstore.unity.com/packages/vfx/particles/unique-projectiles-volume-1-124214) · [Toon Particle Pack SKJ](https://assetstore-fallback.unity.com/packages/vfx/particles/toon-particle-pack-skj-ver-1-76769) · [Combat Magic VFX Vol. 1](https://assetstore-fallback.unity.com/packages/vfx/particles/combat-magic-vfx-vol-1-106836) · [Mobile Cartoon FX](https://marketplace.unity.com/packages/vfx/particles/mobile-cartoon-fx-17717) · [Brackeys VFX Bundle](https://brackeysgames.itch.io/brackeys-vfx-bundle) · [Ray Wang RPG VFX bundle](https://wangray0110.itch.io/unity-rpg-game-vfx-bundle) · [Ray Wang free collection](https://raywang4.gumroad.com/l/rknsi) · [MagusVFX Slashes & Impacts](https://magusvfx.itch.io/2d-vfx-pack-slashes-and-impacts) · [MagusVFX Casting & Buffs](https://magusvfx.itch.io/2d-vfx-pack-casting-and-buffs) · [ME.UITK.Particles](https://github.com/chromealex/me-uitk-particles/wiki/EN) · [UIParticle (uGUI)](https://awesome.ecosyste.ms/projects/github.com%2Fmob-sakai%2Fparticleeffectforugui)
