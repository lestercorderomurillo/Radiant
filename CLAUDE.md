# Radiant Engine

MonoGame C# 2D engine: ECS + GPU-instanced shapes + HRC global illumination.
.NET 8.0 WindowsDX. MonoGame 3.8.5-preview.1. Unsafe enabled. SQLite 1.0.118.

---

## Rules

- **Always update this file** after modifying the codebase (renames, new files, API changes, etc.).

---

## Code Style

| Rule | Example |
|------|---------|
| PascalCase (fields, params, properties, constants) | `int MaxZLayers = 65536;` |
| No `_prefix`, `camelCase`, or `m_` | Exception: GPU constants like `SHAPE_RECT` |
| `static readonly` for arrays/complex | `static readonly int[] ProbeScales = [4, 3, 2, 1];` |
| Collection expressions `[]` | `new[] {}` only when inference needs help |
| Expression-bodied `=>` for one-liners | `public bool IsDebug => Mode != None;` |
| File-scoped namespaces | `namespace com.radiant.engine.bundle;` |
| `var` when type is obvious | Explicit type when needed for clarity |
| `ref` returns for mutation | `ref var transform = ref ECS.GetComponent<Transform>(entityId);` |
| Usings order | `System.*`, `com.radiant.*`, then `Microsoft.Xna.*` |
| No blank lines between same-kind fields | Blank line only between logical groups |
| **Descriptive local variable names** | `transform`, `material`, `circle` — never `t`, `m`, `c` |

The last rule is critical: **never use single-letter or abbreviated variable names**. Use the full type name (lowercased) for local component refs to avoid shadowing the type:

```csharp
// CORRECT — local refs use lowercase type name
ref var transform = ref ECS.GetComponent<Transform>(entityId);
ref var material = ref ECS.GetComponent<Material>(entityId);
ref var circle = ref ECS.GetComponent<Circle2D>(entityId);

// Also correct — contextual prefix when multiple entities in scope
ref var playerTransform = ref ECS.GetComponent<Transform>(playerId);
ref var ghostMaterial = ref ECS.GetComponent<Material>(ghostId);

// WRONG — single letters / abbreviations
ref var t = ref ECS.GetComponent<Transform>(id);
ref var m = ref ECS.GetComponent<Material>(id);
ref var mat = ref ECS.GetComponent<Material>(id);
```

Note: PascalCase applies to fields, properties, methods, parameters, and constants. Local variables use camelCase of the type/descriptive name to avoid type shadowing.

---

## Directory Structure

```
radiant/
├── Content/
│   ├── fonts/BaseFont.spritefont
│   ├── Ghost.png, Eyes.png              # Pac-Man textures (premultiplied alpha)
│   ├── shaders/
│   │   ├── Geometry.fx                  # SDF/JFA generation + debug visualization
│   │   ├── InstancedShapes.fx           # GPU-instanced 2D shape rendering
│   │   ├── ColorManagement.fx           # Tonemapping (None/ACES/ACES2/AgX)
│   │   ├── HRC/                         # HRC GI shaders
│   │   │   ├── HRC_Extensions.fx        #   Ray extension cascade N-1 → N
│   │   │   ├── HRC_FluenceSum.fx        #   Average 4 frustums → final
│   │   │   ├── HRC_FrustumSeed.fx       #   Seed cascade 0 from scene
│   │   │   └── HRC_MergingCones.fx      #   Backward cone merge N → 0
│   │   ├── RCGI/RCGI.fx                 # Alternative ray-march GI
│   │   └── UDR/UDR1.fx, UDR2.fx, UDR3.fx  # Ultra Dynamic Range upscalers
│   └── Content.mgcb                     # Pipeline config (HiDef, Windows)
│
├── src/com.radiant.engine/
│   ├── core/
│   │   ├── ECS.cs              (~612 lines)  # Entity Component System
│   │   ├── Archetype.cs                       # Dense component storage arrays
│   │   ├── Renderer.cs         (~1884 lines)  # Fluent rendering API
│   │   ├── Scene.cs                           # Scene lifecycle (SetupECS → SetupScene)
│   │   ├── System.cs                          # Abstract system + RunAfter/RunBefore/Pausable attrs
│   │   ├── SystemGroup.cs                     # Toggle between mutually exclusive systems
│   │   ├── Shape.cs                           # GPU shape struct (24 bytes, 4 types)
│   │   ├── SpatialIndex.cs                    # Grid spatial hashing (cell=64)
│   │   ├── PagedBitSet.cs                     # 1-bit-per-entity tracking
│   │   └── Interfaces/
│   │       ├── Component.cs                   # Marker interface for ECS components
│   │       └── GameObject.cs                  # IGameObject lifecycle interface
│   │
│   ├── bundle/
│   │   ├── Components/
│   │   │   ├── Spatial/Transform.cs           # Position/Rotation/Scale (Vector3)
│   │   │   ├── 2D/                            # Camera2D, Circle2D, Rectangle2D, Triangle2D,
│   │   │   │                                  # Collision2D, Movement2D, RigidBody2D
│   │   │   ├── 3D/                            # Chunk3D (16³ voxels), Tile3D
│   │   │   └── GPU/                           # Material, MotionTrackable
│   │   ├── Extensions/
│   │   │   ├── LightFactory.cs                # CreateLight(), SpawnRandom(), HueToRGB()
│   │   │   └── VectorExtensions.cs            # Add/Sub/Mul/Div/Apply for Vector3
│   │   └── Systems/
│   │       ├── 2D/
│   │       │   ├── Geometry/Geometry.cs        (~828 lines) # Shape collection + SDF/JFA
│   │       │   ├── HRCGI/HRCGI.cs             (~318 lines) # HRC Global Illumination
│   │       │   ├── RCGI/RCGI.cs                             # Radiance Cascades GI (alt)
│   │       │   ├── Tileset/Tileset.cs                       # 2D infinite tile world
│   │       │   ├── Tileset/TileTypes.cs                     # Tile definitions + TileData component
│   │       │   ├── WorldGen/WorldGen.cs                     # Terrain generation
│   │       │   ├── PerlinNoise2D/PerlinNoise.cs
│   │       │   ├── MouseLight/MouseLight.cs
│   │       │   ├── PaintBrush/PaintBrush.cs
│   │       │   ├── MazeBuilder/
│   │       │   │   ├── PacmanMazeBuilder.cs                 # Maze layout + coin tracking
│   │       │   │   ├── PacmanMazeGenerator.cs               # Procedural maze generation
│   │       │   │   └── PacmanLevelConfig.cs                 # Per-level configuration
│   │       │   └── AI/Pacman/
│   │       │       ├── GhostAI/PacmanGhostAI.cs             # Ghost AI (scatter/chase/frightened)
│   │       │       ├── Player/PacmanPlayer.cs               # Arrow-key player + coins + HUD
│   │       │       └── RainbowGhost/RainbowGhostAI.cs       # Rainbow ghost (clone/merge)
│   │       ├── 3D/Tileset3D/Tileset3D.cs                    # 3D tilemap (placeholder, empty)
│   │       ├── FX/
│   │       │   ├── ColorManagement/ColorManagement.cs       # Tonemapping post-process
│   │       │   └── UDR/                                     # Bilinear.cs, UDR1-3.cs, UDRQuality.cs
│   │       └── UI/
│   │           ├── Gizmos/Gizmos.cs + GizmosRenderer.cs     # Debug overlay (lines/circles/arcs/rects/text)
│   │           ├── UIWindow/UITypes.cs + Inspector.cs        # Retained-mode UI windows
│   │           └── Profiler/PerformanceMonitor.cs            # FPS/CPU/GPU/RAM stats
│   │
│   ├── runtime/
│   │   ├── GameClient.cs              # Entry: creates Window + GameLoop + Scene
│   │   ├── GameLoop.cs   (~212 lines) # 144 FPS / 64 UPS, frame pacing
│   │   ├── GameServer.cs              # TCP server + lobby
│   │   └── Window.cs                  # MonoGame Game subclass (3360x1890)
│   │
│   ├── mplay/                         # Multiplayer (TCP + HTTP lobby, not actively used)
│   │   ├── NetworkClient.cs
│   │   ├── NetworkManager.cs
│   │   └── NetworkMessage.cs
│   │
│   └── tests/2D/
│       ├── PacmanMazeLevelScene.cs    # Pac-Man demo (6 levels, Inspector controls)
│       ├── SimpleLightScene.cs        # Single warm light
│       └── TilesetScene.cs           # Tile world demo
│
└── Program.cs                         # Entry point → GameClient.Run()
```

---

## ECS (Entity Component System)

Archetype-based ECS with 64-bit bitmask signatures (max 64 component types), parallel job system, and spatial indexing.

### Creating and Destroying Entities

```csharp
// Create a bare entity (no components)
int entityId = ECS.CreateEntity();

// Create with position (auto-adds Transform + inserts into SpatialIndex)
int entityId = ECS.CreateEntity(new Vector3(100, 200, 0));

// Destroy — removes from spatial index, recycles ID via stack
ECS.DestroyEntity(entityId);
```

### Adding and Querying Components

All components are `struct : Component`. `AddComponent` returns a ref so you can set fields immediately. `GetComponent` also returns a ref — **always capture by ref when mutating**.

```csharp
// Add a component — returns ref to the newly added component
ref var transform = ref ECS.AddComponent<Transform>(entityId);
transform.Position = new Vector3(100, 200, 0);

// Get a component — returns ref for in-place mutation
ref var material = ref ECS.GetComponent<Material>(entityId);
material.Albedo = Color.White;
material.Emissive = new Color(255, 200, 100, 128);

// Check if entity has a component
bool hasCircle = ECS.HasComponent<Circle2D>(entityId);

// Update position (also updates SpatialIndex)
ECS.SetPosition(entityId, new Vector3(150, 250, 0));
```

### Parallel Queries

Queries match all archetypes containing the requested components and distribute work across `ProcessorCount` threads.

```csharp
// 1-component query
ECS.Query<Transform>((threadId, entity, ref transform) =>
{
    transform.Position.X += 1f;
});

// 2-component query
ECS.Query<Transform, Material>((threadId, entity, ref transform, ref material) =>
{
    material.Emissive = new Color(transform.Position.X / 3840f, 0, 0, 255);
});

// 3-component query
ECS.Query<Transform, Circle2D, Material>((threadId, entity, ref transform, ref circle, ref material) =>
{
    circle.Radius = transform.Scale.X * 10f;
});
```

### Spatial Queries

Grid-based spatial index (cell size = 64 units, sparse dictionary, max 256 entities/cell). Returns `ReadOnlySpan<int>` (zero-allocation).

```csharp
// All entities within radius
ReadOnlySpan<int> nearby = ECS.InRadius(center, radius);

// All entities in axis-aligned box
ReadOnlySpan<int> inBox = ECS.InBox(min, max);

// Entity at exact position (0.01 precision)
int? exact = ECS.AtExact(position);

// K-nearest neighbors
ReadOnlySpan<int> nearest = ECS.Spatial.Nearest(center, count, maxRadius);

// 2D radius query (ignores Y)
ReadOnlySpan<int> flat = ECS.Spatial.InRadius2D(centerX, centerZ, radius);
```

### Pausing

```csharp
ECS.Paused = true;  // Skips Update/FixedUpdate ONLY for systems marked [Pausable]
```

Systems without `[Pausable]` keep running (e.g., Inspector, Gizmos, rendering systems).

### System Retrieval

```csharp
var geometry = ECS.GetSystem<Geometry>();  // Returns null if not registered
```

### Components Reference

All components are `struct : Component`. Reference type fields (like `Texture2D`) are safe — ECS uses managed `Array.Copy`.

| Component | Fields | Notes |
|-----------|--------|-------|
| `Transform` | Position, Rotation, Scale (Vector3) | Rotation.X used as facing direction in some systems |
| `Camera2D` | Position (Vector2), Zoom (float), Rotation (float) | |
| `Material` | Albedo, Emissive (Color), Texture (Texture2D?) | Auto-calculates Absorption and EmissiveScaled on set |
| `Circle2D` | Radius (float) | |
| `Rectangle2D` | Size (Vector2) | |
| `Triangle2D` | Size (Vector2), Bordered (bool) | |
| `Collision2D` | Bounds (Vector2) | |
| `Movement2D` | Speed, Acceleration (Vector2) | |
| `RigidBody2D` | Weight (float) | |
| `MotionTrackable` | 4-frame Vector3 circular buffer | Push(pos), CalculateVelocity() |
| `Tile3D` | Id (ushort) | Used as array elements in Chunk3D.Tiles[] |
| `Chunk3D` | Tiles (Tile3D[]) — 16x16x16 | Get(x,y,z), Set(x,y,z,tile) |
| `TileData` | X, Y, Layer (int), TileTypeId (string) | Tileset system component |

`Tile3D` (ushort Id) is a Component. Used as array elements inside `Chunk3D.Tiles[]`, not typically as a standalone ECS component on entities.

`Material` is the most complex: setting `Albedo` or `Emissive` auto-recalculates `Absorption` (inverted albedo for non-emitters, scaled emissive for emitters) and `EmissiveScaled` (RGB * intensity). The `Texture` field modulates emissive color when non-null.

---

## System Architecture

### System Base Class

```csharp
public abstract class System
{
    public Scene Scene;
    public Renderer Renderer;
    public GameTime GameTime;
    public bool Enabled = true;
    public virtual RenderLayer RenderLayer => RenderLayer.Gameplay; // Render/LateRender sort order

    public virtual void Initialize() {}   // Called once after ECS.Initialize()
    public virtual void Update() {}       // Called every frame
    public virtual void FixedUpdate() {}  // Called at 64 UPS
    public virtual void Render() {}       // Called every frame (rendering pass)
    public virtual void LateRender() {}   // Called after all Render() calls
    public virtual void OnResize() {}     // Called when window resizes
    public virtual void Dispose() {}      // Called on shutdown
}
```

### Ordering Attributes

```csharp
[RunAfter(typeof(Geometry))]      // This system runs after Geometry
[RunBefore(typeof(GizmosRenderer))] // This system runs before GizmosRenderer
[Pausable]                         // Skipped when ECS.Paused = true
public class MySystem : System { ... }
```

Topological sort (Kahn's algorithm) runs at `ECS.Initialize()`.

### RenderLayer — Render Order Control

`LateRender()` uses a separate render-sorted system list: sorted by `RenderLayer` first, then topological index within each layer. `Update()`/`FixedUpdate()`/`Render()` still use pure topological order. (`Render()` keeps topological order because it has GPU pipeline dependencies — systems switch render targets, and reordering would cause backbuffer content loss on MonoGame WindowsDX.)

```csharp
public enum RenderLayer : byte { World = 0, Gameplay = 1, Overlay = 2, UI = 3 }
```

Override in your system class (default is `Gameplay`):

```csharp
public override RenderLayer RenderLayer => RenderLayer.World;
```

| Layer | Systems |
|-------|---------|
| **World** | Geometry, HRCGI, RCGI, ColorManagement, Bilinear, UDR1, UDR2, UDR3 |
| **Gameplay** | *(default)* PacmanGhostAI, PacmanPlayer, RainbowGhostAI, PacmanMazeBuilder, MouseLight, PaintBrush, Tileset, WorldGen, PerlinNoise |
| **Overlay** | GizmosRenderer, PerformanceMonitor |
| **UI** | Inspector |

### SystemGroup — Mutually Exclusive Systems

For systems that are alternatives to each other (e.g., HRCGI vs RCGI, Bilinear vs UDR1/2/3):

```csharp
// Create group — only one system is enabled at a time
var giGroup = new SystemGroup(
    ("HRCGI", ECS.AddSystem<HRCGI>()),
    ("RCGI",  ECS.AddSystem<RCGI>(enabled: false))
);

giGroup.Toggle();              // Dispose current → Initialize next → Enable it
giGroup.ActiveName;            // "HRCGI" or "RCGI"
giGroup.Active;                // The currently enabled System instance
giGroup.ForEach(system => {}); // Iterate all systems in group
```

### System Init Order

1. Inspector → 2. PerformanceMonitor → 3. Geometry → 4. HRCGI/RCGI → 5. ColorManagement → 6. Bilinear/UDR → 7. PacmanMazeBuilder → 8. PacmanPlayer → 9. PacmanGhostAI → 10. RainbowGhostAI → 11. GizmosRenderer

(Actual order determined by `[RunAfter]`/`[RunBefore]` topological sort)

---

## Renderer API

All rendering goes through the `Renderer` class. **Never access `Device`, `SpriteBatch`, or `Window.Content` directly** (marked `[Obsolete]`).

### Fluent Shader Pipeline

Chain shader setup and rendering into a single expression. `Draw()` renders a fullscreen quad.

```csharp
Renderer
    .Reset()                                      // Clear all state
    .SetShader("HRC/HRC_FrustumSeed")             // Load + activate shader (cached)
    .SetTechnique("Default")                       // Select technique
    .Configure(BlendState.Opaque)                  // Set blend state
    .Configure(SamplerState.PointClamp, slot: 0)   // Set sampler at slot
    .SetTarget(outputRT)                           // Set render target (null = backbuffer)
    .Clear(Color.Black)                            // Clear current target
    .SetParameter("EmissiveTexture", emissiveTex)  // Set shader parameter (typed overloads)
    .SetParameter("ScreenSize", Renderer.ScreenSize)
    .Draw()                                        // Draw fullscreen quad
    .Commit();                                     // End any pending SpriteBatch
```

### Shader Management

```csharp
Renderer.SetShader("HRC/HRC_Extensions");     // Load + set active (cached after first load)
Renderer.SetTechnique("Default");              // Set technique on active shader
Renderer.GetShaderEffect("InstancedShapes");   // Get Effect without setting active (for external params)
Renderer.ReleaseShader("OldShader");           // Dispose + remove from cache
```

### Shader Parameters

All `SetParameter` overloads work on the active shader by default, or pass an explicit `Effect`:

```csharp
// Typed overloads (float, int, bool, Vector2/3/4, Matrix, Texture2D, arrays)
Renderer.SetParameter("BlurRadius", 5.0f);
Renderer.SetParameter("ScreenSize", new Vector2(3840, 2160));
Renderer.SetParameter("InputTexture", someTexture);
Renderer.SetParameter("Weights", new float[] { 0.1f, 0.2f, 0.4f });

// On external Effect
Renderer.SetParameter("Param", value, externalEffect);

// Static helper for external Effects
Renderer.SetParameter(shapeShader, "ViewProjection", viewProjMatrix);
```

### State Configuration

`Configure()` overloads set render state for subsequent draw calls:

```csharp
Renderer.Configure(BlendState.AlphaBlend);          // Blend state
Renderer.Configure(DepthStencilState.None);          // Depth stencil
Renderer.Configure(RasterizerState.CullNone);        // Rasterizer
Renderer.Configure(SamplerState.LinearClamp, 0);     // Sampler at slot 0
Renderer.Configure(SpriteSortMode.Immediate);        // Sort mode for SpriteBatch

// Multiple samplers at once
Renderer.Configure((0, SamplerState.PointClamp), (1, SamplerState.LinearWrap));

// Multiple states by type detection
Renderer.Configure(BlendState.Opaque, SamplerState.PointClamp);
```

### Shape Rendering (GPU Instanced)

Shapes are batched and rendered in a single `DrawInstancedPrimitives` call. Up to 65536 default capacity (auto-grows).

```csharp
// Add shapes to batch (fluent)
Renderer.DrawRect(position, size, color);
Renderer.DrawCircle(center, radius, color);
Renderer.DrawTriangle(position, size, color);
Renderer.DrawTriangleBorder(position, size, color);
Renderer.DrawShape(customShape);

// Flush all batched shapes to a render target
Renderer.FlushShapes(target, clearColor, "Default");
// Techniques: "Default" (AA), "Sharp" (hard), "Emissive" (for GI)

// External buffer — zero copy, for pre-collected shapes
Renderer.FlushShapesExternal(shapeArray, count, target, clearColor, "Emissive");

// Clear without rendering
Renderer.ClearShapes();

// Current batch size
int pending = Renderer.ShapeBatchCount;
```

Shape types: 0=rect, 1=circle, 2=triangle, 3=triangle_border.

### Parallel Shape Collection

For multi-threaded shape gathering (used by Geometry system):

```csharp
// Initialize once (typically in system Initialize)
Renderer.InitializeParallelShapes(threadCount, capacityPerThread);

// Ensure capacity for entity count growth
Renderer.EnsureParallelCapacityForEntities(entityCount);

// Inside parallel work:
Renderer.DrawShapeParallel(threadIndex, shape);
// OR direct buffer access:
Shape[] buffer = Renderer.GetParallelBuffer(threadIndex);
float[] zBuffer = Renderer.GetParallelZBuffer(threadIndex);
buffer[localIndex] = myShape;
zBuffer[localIndex] = zValue;
Renderer.SetParallelCount(threadIndex, count);

// Sort each thread's buffer by Z (call inside parallel work)
Renderer.SortParallelBufferByZ(threadIndex);

// Collect all threads into main Shapes array (call on main thread)
Renderer.CollectParallelShapesSorted(); // k-way merge with min-heap O(N log T)
// OR unsorted:
Renderer.CollectParallelShapes();       // simple Array.Copy concatenation

// Clear all parallel buffers
Renderer.ClearParallelShapes();
```

### Render Targets

```csharp
// Push/pop for nested rendering (saves/restores current targets)
Renderer.PushTargets();
Renderer.SetTarget(myRT);         // Single target (null = backbuffer)
// ... render ...
Renderer.PopTargets();            // Restore previous targets

// Multiple render targets (MRT, up to 4)
Renderer.SetTargets(rt0, rt1);                 // 2 targets
Renderer.SetTargets(rt0, rt1, rt2);            // 3 targets
Renderer.SetTargets(rt0, rt1, rt2, rt3);       // 4 targets

// Clear current target
Renderer.Clear(Color.Black);

// Create render targets (factory — never use new RenderTarget2D(Device, ...) directly)
var rt = Renderer.CreateRenderTarget(width, height, SurfaceFormat.Color,
    DepthFormat.None, RenderTargetUsage.DiscardContents);

var texture = Renderer.CreateTexture(width, height, SurfaceFormat.HalfVector4);
```

Pooled binding arrays (16 pre-allocated per MRT size) to avoid GC.

### Blit — SpriteBatch Convenience

Two semantics — know the difference:

```csharp
// STRETCH to current target's viewport (fullscreen)
// Use for: displaying final output, fullscreen effects
Renderer.Blit(sourceTexture, BlendState.Opaque, SamplerState.PointClamp);

// NATIVE SIZE at origin (no stretch)
// Use for: RT-to-RT copies where sizes may differ
Renderer.Blit(sourceTexture, destinationRT, clearColor, BlendState, SamplerState);
```

For stretch-to-specific-target: `SetTarget(target).Clear(color)` then `Blit(source, blend, sampler)`.

### SpriteBatch Drawing

For complex drawing (UI, tiles, gizmos). Must be wrapped in `BeginDraw`/`EndDraw`:

```csharp
Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend,
    SamplerState.PointClamp, scaleMatrix);

Renderer.DrawSprite(texture, destRect, Color.White);
Renderer.DrawSprite(texture, destRect, sourceRect, Color.White, rotation, origin);
Renderer.DrawString(font, "Score: 100", position, Color.White);

Renderer.EndDraw();
```

### Asset Loading (all cached, shared across systems)

```csharp
Texture2D ghostTex  = Renderer.GetTexture("Ghost");          // Content pipeline asset
SpriteFont font      = Renderer.GetFont("fonts/BaseFont");    // SpriteFont
Texture2D whitePixel = Renderer.GetSolidTexture(Color.White); // 1x1 solid (cached by color+size)
Texture2D circleTex  = Renderer.GetCircleTexture(64);         // AA circle (cached by diameter)
```

### Texture Slots (low-level)

For register-bound shader textures (when named parameters aren't available):

```csharp
Renderer.SetTexture(0, someTexture);                         // Bind to slot 0
Renderer.SetTexture(1, anotherTexture, SamplerState.LinearClamp); // Slot + sampler
Renderer.SetTextures((0, texA), (1, texB));                  // Multiple slots
Renderer.ClearTextures(4);                                   // Clear first N slots
Renderer.UploadToTexture(target, colorData, count);           // Raw pixel upload
Renderer.UploadToTexture(target, colorData, subRegion);       // Upload to sub-region
```

### Ping-Pong Rendering

For multi-pass effects that alternate between two render targets:

```csharp
RenderTarget2D finalOutput = Renderer.PingPong(
    rtA, rtB, passCount,
    beforePass: (passIndex, inputTexture) => { /* set shader params */ },
    afterPass: (passIndex) => { /* optional */ },
    clearColor: Color.Black
);
```

### Subtractive Mask

Erases pixels where mask has alpha: `dest * (1 - mask.alpha)`:

```csharp
Renderer.BlitMask(maskTexture, destRect, rotation, origin);
```

### Screen Properties

```csharp
Renderer.ScreenWidth / ScreenHeight / ScreenSize    // Current viewport (pixels)
Renderer.VirtualWidth / VirtualHeight / VirtualSize  // Fixed 3840x2160 world coords
Renderer.AspectRatio / InverseAspectRatio
Renderer.ScreenDiagonal / ScreenArea
Renderer.ScreenLowerPowerOfTwo / ScreenHigherPowerOfTwo

// Dynamic resolution
Renderer.RenderScale = 0.5f;              // 0.25 to 1.0
Renderer.ScaledWidth / ScaledHeight        // Scaled viewport
Renderer.RenderScaleChanged += (scale) => { /* resize RTs */ };

// Coordinate conversion
Vector2 worldPos = Renderer.ScreenToWorld(mouseScreenPos);
Vector2 screenPos = Renderer.WorldToScreen(entityWorldPos);

// Window state
Renderer.IsActive          // Window focused?
Renderer.GameLoop           // Timing info (FPS, etc.)
Renderer.ViewportBounds     // Current viewport Rectangle
Renderer.HasPendingResize   // True when window was resized → call HandleResize()
Renderer.HandleResize();    // Clears flag + updates screen info
Renderer.ClearBackBuffer(Color.Black);
```

### Flow Control

```csharp
Renderer.Reset();    // Clear all state (blend, shader, drawing flag)
Renderer.Begin();    // Mark as actively drawing
Renderer.Commit();   // End any pending SpriteBatch + mark drawing complete
```

---

## Rendering Pipeline

### Geometry System (Geometry.cs)

Collects all entity shapes each frame and produces the textures that feed GI:

1. **Double-buffered collection** — Write buffer (background threads) / Read buffer (render) swapped each frame
2. **Z-layer bucketing** — 65536 layers x ThreadCount buckets, flattened to render arrays via Array.Copy
3. **Shape rendering** — Emissive shapes → EmissiveTexture, Absorption shapes → AbsorptionTexture
4. **SDF generation** — Jump Flooding Algorithm (JFA) on absorption → SDFTexture (HalfVector2)
5. **Motion vectors** — Per-entity velocity encoded as color → MotionVectorTexture (HalfVector2)
6. **Texture draws** — Material.Texture entities drawn via SpriteBatch (separate from instanced shapes)

Debug mode properties:
- `IsDebugging` — true when any debug visualization is active
- `IsDebugHidingGameplay` — true for debug modes that replace gameplay visuals (SDF, JFA, MotionVectors). False for Emissive/Absorption debug (gameplay eyes still render). Used by PacmanGhostAI, RainbowGhostAI, PacmanPlayer to hide eyes in non-gameplay debug views.

### HRCGI System (HRCGI.cs)

Hierarchical Radiance Caching — based on Rouli Freeman (arXiv:2505.02041). 4-frustum cascade GI:

1. **FrustumSeed** — Seed cascade 0 from Emissive/Absorption textures
2. **Extensions** — Extend rays cascade N-1 → N (merge radiance/transmittance)
3. **MergingCones** — Backward pass N → 0, cone merging for angular coverage
4. **FluenceSum** — Average 4 frustums → FinalTexture

Quality presets via Inspector: ProbeScale 4/3/2/1 (Performance → Native).

### Render Target Formats

| Target | Format | Notes |
|--------|--------|-------|
| EmissiveTexture | Color | Screen-sized |
| AbsorptionTexture | Color | Screen-sized, **PreserveContents** |
| SDFTexture | HalfVector2 | Screen-sized |
| MotionVectorTexture | HalfVector2 | Screen-sized |
| JFATexture1/2 | Vector4 | Reduced (SDFScale=0.25) |
| Vrays/Merge Radiance/Transmittance | HalfVector4 | Per-cascade |
| FrustumRadiance/Transmittance | HalfVector4 | Per-frustum (4) |
| FinalTexture (HRCGI) | HalfVector4 | World-sized, raw linear GI |
| ColorManagement Output | Color | Tonemapped sRGB |

**RenderTargetUsage.PreserveContents**: MonoGame WindowsDX discards render target contents on target switch by default. Use PreserveContents for any RT read later (e.g., AbsorptionTexture).

---

## Shaders

### Critical Rules

1. **Register collision**: InstancedShapes.fx must NOT overlap Geometry.fx registers (t0-t3, s0-s1). Use t4+/s2+ for InstancedShapes texture params. Auto-assign defaults to t0, which collides. Pink SDF = register collision.

2. **SM4.0+ style only**: `Texture2D`, `SamplerState`, `.Sample()` — no legacy `tex2D()`.

3. **MGFX dead-code elimination**: Unused texture params per-technique get eliminated at compile time. Default/Sharp techniques don't reference ShapeTexture → `pass.Apply()` won't touch t4/s2 for those techniques. No dummy texture needed.

4. **Premultiplied alpha**: Content pipeline sets `PremultiplyAlpha=True`, meaning `texColor.rgb = originalRGB * texColor.a`. Correct compositing:
   ```hlsl
   result.rgb = texPremul.rgb * tint.rgb * (tintA * sdfAlpha);
   result.a   = texA * tintA * sdfAlpha;
   ```
   Do NOT do `color.rgb *= color.a` after sampling a premultiplied texture — that double-premultiplies.

### InstancedShapes.fx Techniques

| Technique | Description |
|-----------|-------------|
| `Default` | SDF shapes with smoothstep AA (fwidth) |
| `Sharp` | Pixel-perfect, hard discard (no anti-aliasing) |
| `Emissive` | Sharp SDF for GI light source feeding |

### Geometry.fx Techniques

InitializeJFA, InitializeJFAInterior, JFAPass, GenerateSDFFromJFA, DebugSDFVisible, DebugJFA, DebugJFARaw, DebugEmissive, DebugMotionVectors, ClearMotion

---

## UI System (Inspector)

Retained-mode window system: draggable panels, title bars, close buttons, and interactive widgets. **Static API** — safe to call even if Inspector system isn't registered (calls silently ignored). Renders in `LateRender()` before GizmosRenderer. Default window width: 340.

### Window Management

```csharp
Inspector.CreateWindow("myWindow", "Window Title"); // Create auto-positioned window
Inspector.DestroyWindow("myWindow");                 // Remove window entirely
Inspector.ShowWindow("myWindow");                    // Make visible
Inspector.HideWindow("myWindow");                    // Make invisible
Inspector.ToggleWindow("myWindow");                  // Toggle visibility
bool visible = Inspector.IsWindowVisible("myWindow");
```

### Widgets (call in Initialize or SetupScene)

```csharp
// Static text label (can display stats)
Inspector.AddLabel("myWindow", "fpsLabel", "FPS: 0");

// Clickable button
Inspector.AddButton("myWindow", "resetBtn", "Reset", () =>
{
    // callback when clicked
});

// Boolean toggle with initial value
Inspector.AddToggle("myWindow", "debugToggle", "Show Debug", false, (isEnabled) =>
{
    // callback with new bool value
});

// Float slider with range
Inspector.AddSlider("myWindow", "speedSlider", "Speed", 0f, 10f, 5f, (value) =>
{
    // callback with new float value
});

// Remove a widget
Inspector.RemoveWidget("myWindow", "oldWidget");
```

### Updating Widget Values (call in Update)

```csharp
Inspector.SetLabel("myWindow", "fpsLabel", $"FPS: {currentFps:F0}");
Inspector.SetSliderValue("myWindow", "speedSlider", newSpeed);
Inspector.SetToggleValue("myWindow", "debugToggle", true);
```

### Input Gating

```csharp
// Prevent world clicks when mouse is over UI
if (!Inspector.IsMouseOverUI())
{
    // Handle world interaction (clicks, painting, etc.)
}
```

### Events

```csharp
// Fired when F1 restores all windows to visible
Inspector.WindowsRestored += () => { /* update window visibility */ };
```

---

## GizmosRenderer

Debug visual overlay. Add gizmo primitives from any system; they render in `LateRender()`. Queues are cleared each frame in `Update()`.

```csharp
var gizmos = ECS.GetSystem<GizmosRenderer>();
gizmos.ToggleGizmos();  // Toggle visibility

gizmos.AddGizmoLine(start, end, Color.Red, thickness);
gizmos.AddGizmoCircle(center, radius, Color.Green);
gizmos.AddGizmoArc(center, radius, startAngle, endAngle, Color.Blue);
gizmos.AddGizmoRect(rectangle, Color.Yellow, filled);
gizmos.AddGizmoText(position, "debug text", Color.White);
```

Always renders build tag in bottom-left corner regardless of toggle state.

---

## Scene System

```csharp
public class MyScene : Scene
{
    public override void SetupECS()
    {
        // Register systems — order doesn't matter, RunAfter/RunBefore handles it
        ECS.AddSystem<Inspector>();
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<Geometry>();
        ECS.AddSystem<HRCGI>();
        ECS.AddSystem<ColorManagement>();
        ECS.AddSystem<Bilinear>();
        ECS.AddSystem<GizmosRenderer>();
        base.SetupECS(); // triggers topological sort + Initialize() on all systems
    }

    public override void SetupScene()
    {
        // Create entities and configure the world
        int lightId = LightFactory.CreateLight(ECS,
            new Vector2(1920, 1080), 50f,    // position, radius
            Color.White,                      // albedo
            new Color(255, 200, 100, 200));   // emissive (A = intensity)

        base.SetupScene();
    }

    // Optional overrides
    public override void Update() { }       // Called before ECS.Update
    public override void FixedUpdate() { }  // Called before ECS.FixedUpdate
    public override void Render() { }       // Called before ECS.Render
    public override void LateRender() { }   // Called before ECS.LateRender
}
```

`Scene` properties: `ECS`, `Renderer`, `GameTime`, `DeltaTime`.

---

## LightFactory — Entity Creation Helper

```csharp
// Create a light entity (Transform + Circle2D + Material + optional texture)
int lightId = LightFactory.CreateLight(ECS,
    position,     // Vector2
    radius,       // float
    albedo,       // Color — body color
    emissive,     // Color — glow color (A = intensity)
    z,            // float? — Z layer (default = entity ID)
    texture);     // Texture2D? — modulates emissive

// Spawn many random lights
LightFactory.SpawnRandom(ECS, count, screenSize, radius);

// Hue (0-1) to RGB
Color saturated = LightFactory.HueToRGB(0.5f);
```

---

## Game Loop

- **144 FPS** render, **64 UPS** fixed update
- Sleep + spin-wait frame pacing (avoids 15ms Windows scheduler quantum)
- Fixed update accumulator with 8-iteration cap (spiral-of-death prevention)
- FPS tracking: EMA smoothing (0.9 factor), 0.5s update interval

---

## Controls

| Key | Action |
|-----|--------|
| F1 | Toggle all Inspector windows (restore closed windows on show) |
| Arrow keys | Move Pac-Man player + collect coins |
| ESC | Exit application |

All other controls (debug modes, quality cycling, GI/upscaler toggle, level cycling, light spawning, tonemapping) are exposed via Inspector windows with buttons, sliders, and toggles. Each system creates its own window in `Initialize()`.

---

## Key Design Patterns

- **Struct components** — `Component` marker interface, ECS constrains `where T : struct, Component`
- **Reference fields in structs** — `Material.Texture`, `Chunk3D.Tiles` — safe because ECS uses managed `Array.Copy`/`Array.Resize`
- **Double-buffering** — Geometry and Tileset swap read/write buffers each frame to avoid contention
- **Zero-allocation hot paths** — PagedBitSet (1 bit/entity), span-based spatial queries, pooled RT binding arrays, StringBuilder reuse, cycle sort for in-place reordering
- **GPU instancing** — Single `DrawInstancedPrimitives` call for up to 65k shapes via DynamicVertexBuffer
- **Centralized assets** — All textures via `Renderer.GetTexture()`, fonts via `Renderer.GetFont()`, shaders via `Renderer.SetShader()`/`GetShaderEffect()`. Never access `Window.Content` directly
- **Renderer wraps all MonoGame** — `Window`, `Device`, `SpriteBatch` are `[Obsolete]`. Systems use Renderer API exclusively
- **Inspector for all UI** — Static API, each system creates its own window in `Initialize()`. Silently ignored if Inspector not registered. `Inspector.IsMouseOverUI()` for input gating
- **GizmosRenderer** — Visual overlay only. Lines/circles/arcs/rects/text. No stats display (all stats in Inspector windows)

---

## Not Implemented

Audio, physics engine, particles, skeletal animation, save/load, logging, asset hot-reload.
