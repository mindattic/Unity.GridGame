# Effect prefab sources (GG-T269)

Every asset under `Assets/Gaiden/Vfx/` and `Assets/Gaiden/Resources/Vfx/` comes from the owner's archived
**GridGame2026** project (github.com/mindattic/GridGame2026: `Assets/VisualEffects`, plus the materials, textures,
meshes and shaders those prefabs reference). They're imported with their original GUIDs, and the asset packs are
licensed to the owner (including the Vefects and Gamestar material families). The game uses no paid third-party
pack beyond that.

**Allowed future sources:** free/CC0 only, until the owner says otherwise. The candidates, including the CC0
Brackeys VFX Bundle, are in `docs/research/VfxAssetPacks.md`. Record each new pack here with its licence and URL.

**Changes made on import (`Assets/Gaiden/Editor/VfxImport.cs`):**
- removed the old `VisualEffectInstance` script and AudioSources (sound comes from cues)
- set particle systems to Hierarchy scaling
- moved 59 materials that used URP-only shaders to the legacy particle shaders
- gave renderers with no material `Materials/GaidenDefaultParticle.mat`
