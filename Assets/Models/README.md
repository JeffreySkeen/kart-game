# Blender → Unity Asset Workflow

This project targets **Unity 6000.6.7f1** (Unity 6) and **Blender 5.2 LTS**.

## Folder layout

```
Assets/Models/
├── _Source/      # Raw .blend working files (NOT the imported mesh)
├── Karts/        # Player/AI kart meshes
├── Characters/   # Drivers / rigged characters
├── Track/        # Track sections, road pieces, terrain meshes
└── Props/        # Static scenery: cones, barriers, trees, signs, items
```

`_Source` holds your editable `.blend` files. Keep exported FBX (or committed
`.blend` imports) in the category folders alongside their textures.

---

## Two workflows (both supported)

### A. FBX export (recommended for a shared git repo)

Most portable — no dependency on Blender being installed on every machine.

**In Blender:** `File → Export → FBX (.fbx)` and set:

| Setting | Value | Why |
| --- | --- | --- |
| **Path Mode** | Copy + embed textures (or Copy) | Keeps textures with the mesh |
| **Limit to → Selected Objects** | On (if exporting one asset) | Avoids exporting the whole scene |
| **Transform → Scale** | `1.0` | Leave at 1; fix via "Apply Transform" below |
| **Transform → Apply Scalings** | `FBX All` | Prevents double-scaling in Unity |
| **Transform → Forward** | `-Z Forward` | Matches Unity's forward |
| **Transform → Up** | `Y Up` | Blender is Z-up, Unity is Y-up |
| **Apply Transform** | **On** | Bakes rotation/scale so model isn't rotated -90° X in Unity |
| **Geometry → Smoothing** | `Face` or `Normals Only` | Preserves shading; "Normals Only" is best for Unity 6 |
| **Geometry → Apply Modifiers** | On | Bake subsurf/mirror/etc. into the mesh |
| **Armature → Add Leaf Bones** | Off | Avoids extra junk bones |
| **Armature → Only Deform Bones** | On (rigged only) | Skips control/IK bones |
| **Bake Animation** | On (animated only) | Exports actions/timeline |

Before exporting, in Blender do **`Object → Apply → All Transforms`** (or at least
Rotation & Scale). This, combined with "Apply Transform" above, is what keeps
models upright and correctly sized in Unity.

### B. Direct `.blend` import

Unity can import `.blend` files directly, but it launches Blender in the
background to convert them — **Blender must be installed on the machine doing the
import.** Fine for solo work; riskier for teammates/CI.

- `.blend1` backup files are already in `.gitignore`.
- Still apply transforms in Blender (same as above) for correct orientation.

---

## Unity import settings (per model)

Select the imported asset in Unity and check the **Inspector → Model** tab:

- **Scale Factor**: `1` if you applied transforms + used `FBX All`. If a model
  comes in 100× too big/small, this is the first knob to check.
- **Convert Units**: leave on (default).
- **Bake Axis Conversion**: enable if a model still imports rotated.
- **Mesh Compression**: `Off` while iterating; raise later for build size.
- **Materials → Material Creation Mode**: `Standard` — then remap to URP
  materials (this project uses the Universal Render Pipeline).
- **Rig** (rigged assets): set **Animation Type** to `Humanoid` for drivers or
  `Generic` for karts/mechanical parts.

### A note on materials + URP

This project uses URP. Imported Blender materials come in as Standard and will
look pink/unlit until converted. Use **Edit → Rendering → Materials → Convert
All Built-in Materials to URP**, or assign URP/Lit materials manually.

---

## Quick sanity checklist

1. Applied all transforms in Blender.
2. Exported FBX with `Y Up`, `-Z Forward`, Apply Transform on.
3. Dropped the FBX into the right `Assets/Models/<category>` folder.
4. In Unity, confirmed scale (`1`) and upright orientation.
5. Converted materials to URP.
