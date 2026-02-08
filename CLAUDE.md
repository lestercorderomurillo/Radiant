# Radiant Engine — Codebase Reference

## Overview
MonoGame C# 2D game engine with ECS architecture, GPU-instanced shape rendering, and advanced global illumination (HRC). Target: .NET 8.0 Windows DirectX. Uses MonoGame.Framework.WindowsDX 3.8.5-preview.1. Unsafe code enabled. Also depends on System.Data.SQLite.Core 1.0.118.

## Directory Structure
```
radiant/
├── Content/                          # Assets & shaders
│   ├── fonts/BaseFont.spritefont
│   ├── shaders/
│   │   ├── Geometry.fx               # SDF/JFA, debug viz
│   │   ├── InstancedShapes.fx        # GPU-instanced 2D shapes
│   │   ├── HRC/                      # Hierarchical Radiance Caching GI
│   │   │   ├── HRC_Extensions.fx
│   │   │   ├── HRC_FluenceSum.fx
│   │   │   ├── HRC_FrustumSeed.fx
│   │   │   └── HRC_MergingCones.fx
│   │   ├── RCGI/RCGI.fx              # Older ray-march GI (alternative)
│   │   └── UDR/UDR1-3.fx            # Ultra Dynamic Range upscalers
│   └── Content.mgcb                  # Content pipeline (HiDef, Windows)
├── src/com.radiant.engine/
│   ├── core/                         # Engine core
│   │   ├── ECS.cs                    # Entity Component System (~800 lines)
│   │   ├── Archetype.cs              # Dense component storage
│   │   ├── Renderer.cs               # Fluent rendering API (~1716 lines)
│   │   ├── Scene.cs                  # Scene lifecycle
│   │   ├── System.cs                 # System base + RunAfter/RunBefore
│   │   ├── SystemGroup.cs            # Toggle between system variants
│   │   ├── Shape.cs                  # GPU shape struct (24 bytes)
│   │   ├── SpatialIndex.cs           # Grid spatial hashing (cell=64)
│   │   ├── PagedBitSet.cs            # 1-bit-per-entity tracking
│   │   └── Interfaces/
│   │       ├── Component.cs          # Marker interface
│   │       └── GameObject.cs         # IGameObject lifecycle
│   ├── bundle/
│   │   ├── Components/
│   │   │   ├── Spatial/Transform.cs  # Position/Rotation/Scale (Vector3)
│   │   │   ├── 2D/                   # Camera2D, Circle2D, Rectangle2D, Triangle2D, Collision2D, Movement2D, RigidBody2D
│   │   │   ├── 3D/                   # Chunk3D (16³ voxels), Tile3D
│   │   │   └── GPU/                  # Material (Albedo/Emissive/Texture), MotionTrackable (4-frame buffer)
│   │   ├── Extensions/
│   │   │   ├── LightFactory.cs       # CreateLight(), SpawnRandom(), HueToRGB()
│   │   │   └── VectorExtensions.cs   # Add/Sub/Mul/Div/Apply for Vector3
│   │   └── Systems/
│   │       ├── 2D/
│   │       │   ├── Geometry/Geometry.cs       # Shape collection, SDF/JFA, motion vectors (~840 lines)
│   │       │   ├── HRCGI/HRCGI.cs             # HRC Global Illumination (~400 lines)
│   │       │   ├── RCGI/RCGI.cs               # Radiance Cascades GI (alternative)
│   │       │   ├── Tileset/Tileset.cs          # 2D infinite tile world + lighting thread
│   │       │   ├── Tileset/TileTypes.cs        # Tile definitions
│   │       │   ├── WorldGen/WorldGen.cs        # Terrain generation
│   │       │   ├── PerlinNoise2D/PerlinNoise.cs
│   │       │   ├── MouseLight/MouseLight.cs    # Mouse-following light
│   │       │   ├── PaintBrush/PaintBrush.cs    # Brush-based entity painting
│   │       │   ├── MazeBuilder/MazeBuilder.cs  # Pac-Man maze layout
│   │       │   └── AI/Pacman/GhostAI/GhostAI.cs # Ghost AI (scatter/chase/frightened)
│   │       ├── 3D/Tileset3D/Tileset3D.cs       # 3D tilemap (placeholder)
│   │       ├── FX/UDR/                         # Bilinear.cs, UDR1-3.cs, UDRQuality.cs
│   │       └── UI/
│   │           ├── Gizmos/Gizmos.cs + GizmosRenderer.cs  # Debug overlay (F1 toggle)
│   │           └── Profiler/PerformanceMonitor.cs         # FPS/CPU/GPU/RAM stats
│   ├── runtime/
│   │   ├── GameClient.cs             # Entry: creates Window + GameLoop + Scene
│   │   ├── GameLoop.cs               # 144 FPS / 64 UPS, frame pacing (~212 lines)
│   │   ├── GameServer.cs             # TCP server + lobby registration
│   │   └── Window.cs                 # MonoGame Game subclass (3360x1890 default)
│   ├── mplay/                        # Multiplayer (TCP + HTTP lobby)
│   │   ├── NetworkClient.cs
│   │   ├── NetworkManager.cs
│   │   └── NetworkMessage.cs
│   └── tests/2D/                     # Test scenes
│       ├── MazeScene.cs              # Pac-Man demo (Tab=GI, F11=upscaler, X=lights)
│       ├── SimpleLightScene.cs       # Single warm light
│       └── TilesetScene.cs           # Tile world demo
└── Program.cs                        # Entry point
```

## ECS Architecture

### Entity Lifecycle
```csharp
int id = ECS.CreateEntity();                     // or CreateEntity(Vector3) for spatial
ref var t = ref ECS.AddComponent<Transform>(id); // returns ref for mutation
ref var m = ref ECS.GetComponent<Material>(id);
bool has = ECS.HasComponent<Circle2D>(id);
ECS.DestroyEntity(id);                           // recycles ID via stack
```

### Queries (Parallel)
```csharp
ECS.ForEach<Transform>((threadIdx, entity, ref transform) => { ... });
ECS.ForEach<Transform, Material>((threadIdx, entity, ref t, ref m) => { ... });
ECS.Query<Transform, Circle2D, Material>((threadIdx, entity, ref t, ref c, ref m) => { ... });
```
- Signature-based archetype matching (64-bit bitmask, max 64 component types)
- Thread pool = `Environment.ProcessorCount` workers with `ManualResetEventSlim`
- Swap-with-last removal for O(1) entity deletion

### System Ordering
```csharp
[RunAfter(typeof(Geometry))]
[RunBefore(typeof(GizmosRenderer))]
public class MySystem : System { ... }
```
Topological sort (Kahn's algorithm) at `ECS.Initialize()`.

### System Lifecycle
`Initialize()` → `Update()` / `FixedUpdate()` → `Render()` → `LateRender()` → `Dispose()`

### Components (all `struct : Component`)
| Component | Key Fields |
|-----------|-----------|
| `Transform` | Position, Rotation, Scale (Vector3) |
| `Camera2D` | Position (Vector2), Zoom, Rotation |
| `Material` | Albedo, Emissive (Color), Texture (Texture2D?), auto-calc Absorption/EmissiveScaled |
| `Circle2D` | Radius (float) |
| `Rectangle2D` | Size (Vector2) |
| `Triangle2D` | Size (float), Bordered (bool) |
| `Collision2D` | Bounds (Vector2) |
| `Movement2D` | Speed, Acceleration (Vector2) |
| `RigidBody2D` | Weight (float) |
| `MotionTrackable` | 4-frame Vector3 circular buffer |
| `Chunk3D` | 16x16x16 Tile3D[] |
| `Tile3D` | Id (ushort) |

### Spatial Index
```csharp
ReadOnlySpan<int> nearby = ECS.InRadius(center, radius);
ReadOnlySpan<int> box = ECS.InBox(min, max);
int? exact = ECS.AtExact(position);  // 0.01 precision
```
Grid cells = 64 units, sparse dictionary, max 256 entities/cell.

## Renderer API

### Fluent Pattern
```csharp
Renderer.SetShader("MyShader")
    .SetTechnique("Default")
    .SetParameter("Param", value)
    .SetTarget(myRT)
    .Draw();  // fullscreen quad
```

### Shape Rendering (GPU Instanced)
```csharp
Renderer.DrawRect(pos, size, color);
Renderer.DrawCircle(center, radius, color);
Renderer.DrawTriangle(pos, size, color);
Renderer.FlushShapes(target, clearColor, technique);
// External: FlushShapesExternal(shapes[], count, target, clearColor, technique)
```
Shape types: 0=rect, 1=circle, 2=triangle, 3=triangle_border. Default capacity 65536.

### Parallel Shape Collection
```csharp
Renderer.InitializeParallelShapes(threadCount, capacityPerThread);
Renderer.DrawShapeParallel(threadIndex, shape);
Renderer.CollectParallelShapesSorted(); // k-way merge by Z
```

### Render Targets
```csharp
Renderer.PushTargets();
Renderer.SetTarget(rt);       // single
Renderer.SetTargets(rt1, rt2); // MRT (up to 4)
Renderer.PopTargets();
```
Pooled binding arrays (16 pre-alloc per MRT size) to avoid GC.

### Coordinates
- Virtual space: 3840x2160 (fixed world coords)
- `ScreenToWorld(screenPos)` / `WorldToScreen(worldPos)`
- Orthographic projection: `CreateOrthographicOffCenter(0, VirtualW, VirtualH, 0, 0, 1)`

### Texture Helpers
```csharp
Renderer.GetSolidTexture(Color.White);      // cached 1x1
Renderer.GetCircleTexture(diameter);        // cached AA circle
Renderer.SetTexture(slot, texture);         // direct register bind
Renderer.UploadToTexture(target, data[], count);
```

### Ping-Pong
```csharp
Renderer.PingPong(rtA, rtB, passes, beforePass, afterPass, clearColor);
```

## Rendering Pipeline

### Geometry System (Geometry.cs)
1. **Double-buffered collection**: Write buffer (background) / Read buffer (render) swapped each frame
2. **Z-layer bucketing**: 65536 layers × ThreadCount buckets, flattened to render arrays via Array.Copy
3. **Shape rendering**: Emissive shapes → EmissiveTexture, Absorption shapes → AbsorptionTexture
4. **SDF generation**: Jump Flooding Algorithm (JFA) on absorption → SDFTexture (HalfVector2)
5. **Motion vectors**: Velocity encoded as color → MotionVectorTexture (HalfVector2)
6. **Texture draws**: Material.Texture entities drawn via SpriteBatch (separate from instanced shapes)

### HRCGI System (HRCGI.cs)
Based on Rouli Freeman (arXiv:2505.02041). 4-frustum cascade GI:
1. **FrustumSeed** → Seed cascade 0 from Emissive/Absorption textures
2. **Extensions** → Extend rays cascade N-1 → N (merge radiance/transmittance)
3. **MergingCones** → Backward pass N → 0, cone merging for angular coverage
4. **FluenceSum** → Average 4 frustums → FinalTexture

Quality presets (F5): ProbeScale 4/3/2/1 (Performance → Native)

### Render Targets
| Target | Format | Notes |
|--------|--------|-------|
| EmissiveTexture | Color | Screen-sized |
| AbsorptionTexture | Color | Screen-sized, **PreserveContents** |
| SDFTexture | HalfVector2 | Screen-sized |
| MotionVectorTexture | HalfVector2 | Screen-sized |
| JFATexture1/2 | Vector4 | Reduced (SDFScale=0.25) |
| VraysRadiance/Transmittance[C] | HalfVector4 | Per-cascade |
| MergeRadiance/Transmittance[C] | HalfVector4 | Per-cascade |
| FrustumRadiance/Transmittance[F] | HalfVector4 | Per-frustum (4) |
| FinalTexture | Color | World-sized, final GI output |

## Shader Rules (Critical)
- **Register collision**: InstancedShapes.fx must NOT overlap with Geometry.fx (t0-t3, s0-s1). Use t4+/s2+ for InstancedShapes texture params.
- **SM4.0+ style**: `Texture2D`, `SamplerState`, `.Sample()` — no legacy tex2D.
- **MGFX dead-code elimination**: Unused texture params per-technique get eliminated. Default/Sharp don't reference ShapeTexture → pass.Apply() won't touch t4/s2.
- **Premultiplied alpha**: Content pipeline `PremultiplyAlpha=True`. Correct: `result.rgb = texPremul.rgb * tint.rgb * (tintA * sdfAlpha); result.a = texA * tintA * sdfAlpha;` Do NOT double-premultiply.

### Shader Techniques (InstancedShapes.fx)
- `Default` — SDF shapes with smoothstep AA (fwidth)
- `Sharp` — Pixel-perfect, hard discard
- `Emissive` — Sharp SDF for GI feeding

### Geometry.fx Techniques
InitializeJFA, InitializeJFAInterior, JFAPass, GenerateSDFFromJFA, DebugSDFVisible, DebugJFA, DebugJFARaw, DebugEmissive, DebugMotionVectors, ClearMotion

## Game Loop & Timing
- **Target**: 144 FPS render, 64 UPS fixed update
- **Frame pacing**: Sleep + spin-wait (avoids 15ms scheduler quantum)
- **Fixed update**: Accumulator with 8-iteration cap (spiral-of-death prevention)
- **FPS tracking**: EMA smoothing (0.9 factor), 0.5s update interval

## Scene System
```csharp
public class MyScene : Scene {
    public override void SetupECS() {
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<Geometry>();
        ECS.AddSystem<HRCGI>();
        ECS.AddSystem<Bilinear>();
        ECS.AddSystem<GizmosRenderer>();
        base.SetupECS(); // triggers topological sort
    }
    public override void SetupScene() {
        int id = ECS.CreateEntity();
        // add components...
    }
}
```
`SystemGroup` for toggling alternatives (e.g., HRCGI↔RCGI, Bilinear↔UDR1/2/3).

## Debug Controls
| Key | Action |
|-----|--------|
| F1 | Toggle gizmos overlay |
| F2 | Cycle Geometry debug (emissive, absorption, SDF, JFA, motion) |
| F3 | Cycle HRC texture displays |
| F5 | Cycle HRC quality preset |
| F11 | Toggle upscaler (Bilinear ↔ UDR variants) |
| Tab | Toggle GI system (HRCGI ↔ RCGI) |
| X | Spawn random lights (MazeScene) |
| ESC | Exit |

## Key Patterns
- **Struct components** with marker interface, ECS constrains `where T : struct, Component`
- **Reference fields in structs** (e.g., `Material.Texture`, `Chunk3D.Tiles`) — safe because ECS uses managed `Array.Copy`
- **Double-buffering** — Geometry and Tileset swap read/write buffers each frame
- **Object pooling** — RT binding arrays, entity IDs, result arrays, StringBuilders
- **Zero-allocation** — PagedBitSet (1 bit/entity), span-based spatial queries, cycle sort
- **GPU instancing** — Single draw call for up to 65k shapes via DynamicVertexBuffer

## Networking (mplay/)
TCP client/server with HTTP lobby (NetworkManager). JSON serialization. Not heavily used — infrastructure exists but no active gameplay networking.

## Not Implemented
Audio, physics engine, particles, skeletal animation, save/load, logging, asset hot-reload.

## Typical System Init Order
1. PerformanceMonitor → 2. Geometry → 3. HRCGI/RCGI → 4. Bilinear/UDR → 5. MazeBuilder → 6. GhostAI → 7. GizmosRenderer

(Actual order determined by `[RunAfter]`/`[RunBefore]` topological sort)
