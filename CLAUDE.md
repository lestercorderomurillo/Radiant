# Radiant Engine

MonoGame C# 2D engine: ECS + GPU-instanced shapes + HRC global illumination.
.NET 8.0 WindowsDX. MonoGame 3.8.5-preview.1. Unsafe enabled. SQLite 1.0.118. FontStashSharp 1.5.4.

## Rules

- **Always update this file** after modifying the codebase (renames, new files, API changes, etc.).
- **Never create Texture2D or GPU resources directly in systems.** Add to Renderer API instead.

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
| No decorative/banner comments | Never `// --- Section ---`, `// ═══`, `// ======`. Blank lines suffice |
| `/// <summary>` for public API docs | Standard C# XML doc comments on public methods/classes |
| **Descriptive local variable names** | `transform`, `material`, `circle` — never `t`, `m`, `c` |
| **No dead code** | Remove unused fields/variables/usings. Public API awaiting callers is fine |

**Critical**: never use single-letter or abbreviated variable names. Local refs use lowercase type name (camelCase) to avoid type shadowing:

```csharp
ref var transform = ref ECS.GetComponent<Transform>(entityId);       // correct
ref var playerTransform = ref ECS.GetComponent<Transform>(playerId); // correct (contextual prefix)
ref var t = ref ECS.GetComponent<Transform>(id);                     // WRONG
```

## Directory Structure

```
radiant/
├── Content/
│   ├── fonts/Inter-Regular.ttf, Inter-Bold.ttf, PressStart2P.ttf (loaded by FontStashSharp at runtime)
│   ├── Ghost.png, Eyes.png (premultiplied alpha)
│   ├── shaders/
│   │   ├── Geometry.fx, InstancedShapes.fx, ColorManagement.fx, GlassBlur.fx
│   │   ├── HRC/ (HRC_Extensions, HRC_FluenceSum, HRC_FrustumSeed, HRC_MergingCones)
│   │   ├── RCGI/RCGI.fx, UDR/UDR1-3.fx
│   └── Content.mgcb (HiDef, Windows)
│
├── src/com.radiant.engine/
│   ├── core/ (ECS, Archetype, Renderer, Scene, System, SystemGroup, Shape, SpatialIndex, PagedBitSet)
│   ├── bundle/
│   │   ├── Components/
│   │   │   ├── Spatial/Transform.cs
│   │   │   ├── 2D/ (Camera2D, Circle2D, Rectangle2D, Triangle2D, Collision2D, Movement2D, RigidBody2D)
│   │   │   ├── 3D/ (Chunk3D, Tile3D)
│   │   │   └── GPU/ (Material, MotionTrackable)
│   │   ├── Extensions/ (LightFactory, VectorExtensions)
│   │   └── Systems/
│   │       ├── 2D/ Geometry, HRCGI, RCGI, Tileset, WorldGen, PerlinNoise2D,
│   │       │       MouseLight, PaintBrush, MazeBuilder, AI/Pacman
│   │       ├── 3D/ Tileset3D
│   │       ├── FX/ ColorManagement, UDR
│   │       └── UI/ Gizmos, UIWindow, Profiler
│   ├── runtime/ (GameClient, GameLoop, GameServer, Window)
│   ├── mplay/ (NetworkClient, NetworkManager, NetworkMessage)
│   └── tests/2D/ (PacmanMazeLevelScene, SimpleLightScene, TilesetScene)
│
└── Program.cs
```

## ECS

Archetype-based, 64-bit bitmask signatures (max 64 component types), parallel queries, spatial indexing.

### Entities & Components

```csharp
int entityId = ECS.CreateEntity();
int entityId = ECS.CreateEntity(new Vector3(100, 200, 0)); // Auto-adds Transform + SpatialIndex
ECS.DestroyEntity(entityId);

ref var transform = ref ECS.AddComponent<Transform>(entityId); // Returns ref
ref var material = ref ECS.GetComponent<Material>(entityId);   // Returns ref — always capture by ref

bool has = ECS.HasComponent<Circle2D>(entityId);

ECS.SetPosition(entityId, position); // Also updates SpatialIndex
```

### Parallel Queries

1-3 components, distributed across `ProcessorCount` threads:

```csharp
ECS.Query<Transform>((threadId, entity, ref transform) => { });
ECS.Query<Transform, Material>((threadId, entity, ref transform, ref material) => { });
ECS.Query<Transform, Circle2D, Material>((threadId, entity, ref transform, ref circle, ref material) => { });
```

### Spatial Queries

Returns `ReadOnlySpan<int>` (zero-allocation):

```csharp
ECS.InRadius(center, radius);
ECS.InBox(min, max);
ECS.AtExact(position);                    // 0.01 precision

ECS.Nearest(center, count, maxRadius);
```

### Tags

Lightweight string-based entity grouping. Backed by `PagedBitSet` (1 bit per entity). Cleaned up automatically on `DestroyEntity`/`DestroyAllEntities`.

```csharp
ECS.AddTag(entityId, "dummy");
ECS.RemoveTag(entityId, "dummy");
ECS.HasTag(entityId, "dummy");

PagedBitSet tagged = ECS.WithTag("dummy");   // null if tag never used; iterate with foreach
ECS.DestroyEntitiesWithTag("dummy");          // Destroy all + clear tag
ECS.ClearTag("dummy");                        // Remove tag from all without destroying
```

### Pausing & System Retrieval

```csharp
ECS.GameplayPaused = true;  // Skips systems marked [Pausable] or [Pausable(PauseGroup.Gameplay)]
ECS.AnimationPaused = true; // Skips systems marked [Pausable(PauseGroup.Animation)]

var system = ECS.GetSystem<Geometry>(); // Returns null if not registered
```

`[Pausable]` defaults to `PauseGroup.Gameplay`. Systems without the attribute always run.

### Components

| Component | Fields | Notes |
|-----------|--------|-------|
| `Transform` | Position, Rotation, Scale (Vector3) | Rotation.X = facing direction |
| `Camera2D` | Position (Vector2), Zoom, Rotation (float) | |
| `Material` | Albedo, Emissive (Color), Texture (Texture2D?) | Auto-calculates Absorption/EmissiveScaled on set |
| `Circle2D` | Radius (float) | |
| `Rectangle2D` | Size (Vector2) | |
| `Triangle2D` | Size (Vector2), Bordered (bool) | |
| `Collision2D` | Bounds (Vector2) | |
| `Movement2D` | Speed, Acceleration (Vector2) | |
| `RigidBody2D` | Weight (float) | |
| `MotionTrackable` | 4-frame Vector3 circular buffer | Push(pos), CalculateVelocity() |
| `Chunk3D` | Tiles (Tile3D[]) — 16x16x16 | Get/Set(x,y,z,tile) |
| `TileData` | X, Y, Layer (int), TileTypeId (string) | |

`Material`: setting Albedo/Emissive auto-recalculates Absorption and EmissiveScaled. Texture modulates emissive when non-null. Reference fields safe — ECS uses managed `Array.Copy`.

## System Architecture

```csharp
public abstract class System
{
    public Scene Scene;
    public Renderer Renderer;
    public GameTime GameTime;
    public bool Enabled = true;
    public virtual RenderLayer RenderLayer => RenderLayer.Gameplay;

    public virtual void Initialize() {}   // Once after ECS.Initialize()
    public virtual void Update() {}       // Every frame
    public virtual void FixedUpdate() {}  // 64 UPS
    public virtual void Render() {}       // Every frame (GPU pipeline order)
    public virtual void LateRender() {}   // After all Render() — sorted by RenderLayer then topo
    public virtual void OnResize() {}
    public virtual void Dispose() {}
}
```

### Ordering & Attributes

`[RunAfter(typeof(A), typeof(B))]`, `[RunBefore(...)]` — params Type[], AllowMultiple.
`[Pausable]` — skipped when `ECS.GameplayPaused` (default). `[Pausable(PauseGroup.Animation)]` — skipped when `ECS.AnimationPaused`.
Topological sort (Kahn's) at `ECS.Initialize()`. Disabled systems (`Enabled = false`) are skipped during initialization — SystemGroup activates them later via `SetActive()`.

### RenderLayer

`LateRender()` sorted by layer first, then topo index. `Render()` stays pure topo (GPU pipeline deps).

| Layer | Value | Systems |
|-------|-------|---------|
| World | 0 | Geometry, HRCGI, RCGI, ColorManagement, Bilinear, UDR1-3 |
| Gameplay | 1 | *(default)* Game systems |
| Overlay | 2 | GizmosRenderer, PerformanceMonitor |
| UI | 3 | Inspector |

### SystemGroup

Mutually exclusive systems (e.g., HRCGI vs RCGI):

```csharp
var group = new SystemGroup(
    ("HRCGI", ECS.AddSystem<HRCGI>()),
    ("RCGI",  ECS.AddSystem<RCGI>(enabled: false))
);

group.Toggle();              // Dispose current → Initialize next
group.ActiveName;            // Current system name
group.Active;                // Current System instance
group.ForEach(system => {}); // Iterate all systems in group
```

## Renderer API

All rendering through `Renderer`. Never access `Device`, `SpriteBatch`, `Window.Content` directly.

### Fluent Shader Pipeline

```csharp
Renderer
    .Reset()
    .SetShader("HRC/HRC_FrustumSeed").SetTechnique("Default")
    .Configure(BlendState.Opaque).Configure(SamplerState.PointClamp, slot: 0)
    .SetTarget(outputRT).Clear(Color.Black)
    .SetParameter("EmissiveTexture", emissiveTex)
    .SetParameter("ScreenSize", Renderer.ScreenSize)
    .Draw()
    .Commit();
```

**Shader management**:
- `SetShader(name)` — load + activate (cached)
- `SetTechnique(name)` — select technique
- `GetShaderEffect(name)` — get Effect without activating
- `ReleaseShader(name)` — dispose + remove from cache

**SetParameter** (typed overloads: float, int, bool, Vector2/3/4, Matrix, Texture2D, arrays):
- `SetParameter(name, value)` — on active shader
- `SetParameter(name, value, externalEffect)` — on specific Effect
- `SetParameter(effect, name, value)` — static helper

**Configure** (overloads): BlendState, DepthStencilState, RasterizerState, `SamplerState + slot`, SpriteSortMode, tuple pairs `(slot, SamplerState)`, multi-type.

### Shape Rendering (GPU Instanced)

Up to 65536 (auto-grows). Shape types: 0=rect, 1=circle, 2=triangle, 3=triangle_border.

```csharp
Renderer.DrawRect(position, size, color);
Renderer.DrawCircle(center, radius, color);
Renderer.DrawTriangle(position, size, color);
Renderer.DrawTriangleBorder(position, size, color);
Renderer.DrawShape(customShape);

Renderer.FlushShapes(target, clearColor, "Default");                  // "Default" (AA), "Sharp", "Emissive"
Renderer.FlushShapesExternal(array, count, target, clearColor, tech); // Zero-copy external buffer

Renderer.ClearShapes();
Renderer.ShapeBatchCount; // Current batch size
```

### Parallel Shape Collection

```csharp
Renderer.InitializeParallelShapes(threadCount, capacity);
Renderer.EnsureParallelCapacityForEntities(entityCount);

Renderer.DrawShapeParallel(threadIndex, shape);
Renderer.SortParallelBufferByZ(threadIndex);

Renderer.CollectParallelShapesSorted(); // k-way merge
Renderer.CollectParallelShapes();       // unsorted Array.Copy
Renderer.ClearParallelShapes();

// Direct buffer access
Shape[] buffer = Renderer.GetParallelBuffer(threadIndex);
float[] zBuffer = Renderer.GetParallelZBuffer(threadIndex);
Renderer.SetParallelCount(threadIndex, count);
```

### Render Targets

```csharp
Renderer.PushTargets();
Renderer.PopTargets();

Renderer.SetTarget(rt);        // null = backbuffer
Renderer.SetTargets(rt0, rt1); // MRT up to 4
Renderer.Clear(Color.Black);

var rt = Renderer.CreateRenderTarget(w, h, SurfaceFormat.Color,
    DepthFormat.None, RenderTargetUsage.DiscardContents);

var tex = Renderer.CreateTexture(w, h, SurfaceFormat.HalfVector4);
```

### Blit

Two semantics — know the difference:

```csharp
// STRETCH to current target's viewport (fullscreen)
Renderer.Blit(source, BlendState.Opaque, SamplerState.PointClamp);

// NATIVE SIZE at origin (no stretch) — for RT-to-RT copies
Renderer.Blit(source, destRT, clearColor, blend, sampler);

// Stretch-to-specific-target: SetTarget(t).Clear(c) then Blit(source, blend, sampler)

// Subtractive mask: dest * (1 - mask.a)
Renderer.BlitMask(mask, destRect, rotation, origin);
```

### SpriteBatch Drawing

```csharp
Renderer.BeginDraw(SpriteSortMode.Deferred, BlendState.AlphaBlend,
    SamplerState.PointClamp, scaleMatrix);

Renderer.DrawSprite(texture, destRect, Color.White);
Renderer.DrawSprite(texture, destRect, sourceRect, Color.White, rotation, origin);
Renderer.DrawString("Inter", 16f, text, position, color);        // Dynamic font by name + size
Renderer.DrawString("Inter-Bold", 16f, text, position, color);  // Bold variant
Renderer.MeasureString("Inter", 16f, text);                     // Returns Vector2

Renderer.EndDraw();
```

### Fonts (FontStashSharp)

TTF fonts loaded at runtime — no content pipeline. Request any size dynamically.

```csharp
Renderer.LoadFont("MyFont", "fonts/MyFont.ttf");               // Load a TTF (path relative to Content root)
Renderer.GetFont("Inter", 24f);                                // Get SpriteFontBase at specific size
Renderer.DrawString("Inter", 24f, text, position, color);      // Measure + draw in one call
Renderer.DrawString("Inter-Bold", 24f, text, position, color, bold: true); // Faux-bold (double-draw)
Renderer.MeasureString("Inter", 24f, text);                    // Returns Vector2 dimensions
```

Pre-loaded fonts: `"Inter"` (Inter-Regular.ttf), `"Inter-Bold"` (Inter-Bold.ttf), `"PressStart2P"` (PressStart2P.ttf).

### Assets (all cached)

```csharp
Renderer.GetTexture("Ghost");
Renderer.GetSolidTexture(Color.White);   // 1x1 solid (cached by color)
Renderer.GetCircleTexture(64);           // AA circle (cached by diameter)
Renderer.GetRoundedRectTexture(8);       // AA rounded rect (cached by radius)
```

### Rounded Rectangles

```csharp
Renderer.DrawRoundedRect(bounds, color, cornerRadius);
Renderer.DrawRoundedRect(bounds, color, radius, RoundedCorners.Top);
// Flags: None, TL, TR, BL, BR, Top (TL|TR), Bottom (BL|BR), All
```

### Texture Slots

```csharp
Renderer.SetTexture(slot, texture);
Renderer.SetTexture(slot, texture, sampler);
Renderer.SetTextures((0, texA), (1, texB));

Renderer.ClearTextures(count);

Renderer.UploadToTexture(target, data, count);
Renderer.UploadToTexture(target, data, subRegion);
```

### Ping-Pong

```csharp
RenderTarget2D output = Renderer.PingPong(rtA, rtB, passCount,
    beforePass: (index, inputTexture) => { },
    afterPass: (index) => { },
    clearColor: Color.Black);
```

### Screen Properties

```csharp
Renderer.ScreenWidth / ScreenHeight / ScreenSize   // Current viewport (pixels)
Renderer.VirtualWidth / VirtualHeight / VirtualSize // Fixed 3840x2160 world coords

Renderer.RenderScale = 0.5f; // 0.25 to 1.0
Renderer.ScaledWidth / ScaledHeight;
Renderer.RenderScaleChanged += (scale) => {};

Renderer.ScreenToWorld(screenPos);
Renderer.WorldToScreen(worldPos);
Renderer.VirtualToScreenScale;
Renderer.VirtualToScreenRect(x, y, w, h);

Renderer.IsActive;         // True when window is focused
Renderer.ViewportBounds;
Renderer.HasPendingResize;
Renderer.HandleResize();

Renderer.ClearBackBuffer(Color.Black);   // Routes to SceneRT (creates/resizes as needed)
Renderer.PresentToBackBuffer();           // Copies SceneRT → actual backbuffer

Renderer.SceneRT;             // Scene render target (all rendering goes here, not backbuffer)

Renderer.Reset();
Renderer.Begin();
Renderer.Commit();
```

## Rendering Pipeline

### Geometry System

Collects entity shapes → produces textures for GI:

1. Background blit (BackgroundEmissive/BackgroundAbsorption if set)
2. Double-buffered write/read swap each frame
3. Z-layer bucketing (65536 layers x threads)
4. Shape rendering → EmissiveTexture + AbsorptionTexture (append mode with backgrounds)
5. JFA → SDFTexture (HalfVector2)
6. Motion vectors → MotionVectorTexture (HalfVector2)
7. Material.Texture entities via SpriteBatch

Debug: `IsDebugging` (any debug active), `IsDebugHidingGameplay` (SDF/JFA/MotionVectors replace gameplay).

### HRCGI System

HRC GI (arXiv:2505.02041). 4-frustum cascades:
FrustumSeed → Extensions → MergingCones → FluenceSum → FinalTexture.
Quality: ProbeScale 4/3/2/1.

### Render Target Formats

| Target | Format | Notes |
|--------|--------|-------|
| Emissive/AbsorptionTexture | Color | AbsorptionTexture uses **PreserveContents** |
| SDF/MotionVectorTexture | HalfVector2 | Screen-sized |
| JFATexture1/2 | Vector4 | SDFScale=0.25 |
| Cascade/Frustum RTs | HalfVector4 | Per-cascade/frustum |
| FinalTexture (HRCGI) | HalfVector4 | World-sized, raw linear |
| ColorManagement Output | Color | Tonemapped sRGB |

**PreserveContents**: MonoGame WindowsDX discards RT on target switch by default. Use for any RT read later.

## Shaders

1. **Register collision**: InstancedShapes.fx must NOT overlap Geometry.fx registers (t0-t3, s0-s1). Use t4+/s2+. Auto-assign defaults to t0 = collision. Pink SDF = register collision.
2. **SM4.0+ only**: `Texture2D`, `SamplerState`, `.Sample()` — no `tex2D()`.
3. **MGFX dead-code elimination**: Unused texture params per-technique eliminated. Default/Sharp don't reference ShapeTexture → pass.Apply() skips t4/s2.
4. **Premultiplied alpha**: `texColor.rgb = origRGB * texColor.a`. Don't double-premultiply.
   Correct: `result.rgb = texPremul.rgb * tint.rgb * (tintA * sdfAlpha); result.a = texA * tintA * sdfAlpha;`

**InstancedShapes.fx**: Default (AA), Sharp (hard), Emissive (GI feed).

**Geometry.fx**: InitializeJFA, InitializeJFAInterior, JFAPass, GenerateSDFFromJFA, DebugSDFVisible, DebugJFA, DebugJFARaw, DebugEmissive, DebugMotionVectors, ClearMotion.

**GlassBlur.fx**: Kawase blur (4-pass diagonal sampling). Used by Inspector for frosted glass window backgrounds. Params: `InputTexture`, `TexelSize`, `BlurOffset`.

## Inspector (UI)

Static API — safe even if Inspector not registered. Retained-mode windows, 375px default width, Inter font at 16px via FontStashSharp. Default theme: Radiant.

### Windows

```csharp
Inspector.CreateWindow("id", "Title");
Inspector.CreateWindow("id", "Title", layoutOrder);

Inspector.DestroyWindow("id");

Inspector.ShowWindow("id");
Inspector.HideWindow("id");
Inspector.ToggleWindow("id");

Inspector.IsWindowVisible("id");
```

### Widgets (call in Initialize or SetupScene)

```csharp
Inspector.AddLabel("win", "id", "text");
Inspector.AddButton("win", "id", "text", () => {});
Inspector.AddToggle("win", "id", "text", initialValue, (bool value) => {});
Inspector.AddSlider("win", "id", "text", min, max, initial, (float value) => {});
Inspector.AddDropdown("win", "id", "text", options, initialIndex, (int index) => {});
Inspector.RemoveWidget("win", "id");
```

### Updating Widgets (call in Update)

```csharp
Inspector.SetLabel("win", "id", "new text");
Inspector.SetSliderValue("win", "id", value);
Inspector.SetToggleValue("win", "id", value);
Inspector.SetDropdownValue("win", "id", index);
Inspector.SetDropdownOptions("win", "id", newOptions);
Inspector.SetWidgetEnabled("win", "id", enabled); // Greyed out + non-interactive when disabled
```

### Input & Themes

```csharp
Inspector.IsMouseOverUI(); // Input gating

Inspector.RegisterTheme("name", new InspectorTheme { ... });
Inspector.ApplyTheme("name");
Inspector.GetThemeNames();

Inspector.WindowsRestored += () => {}; // Fired by Workspace > Reorder Windows
```

Built-in themes: Solaris, Carbon, Midnight, Sentinel (light), Greenfields (olive), Neon, Nord.

### Menu Bar

F1 toggles a top-of-screen menu bar with **About** and **Workspace** menus. About has a single "About Radiant" action that opens a centered window with engine/author info. Workspace dynamically lists all registered Inspector windows with show/hide toggles plus a "Reorder Windows" action (triggers auto-layout). The workspace menu rebuilds each time it opens — disabled systems (via SystemGroup) never create windows, so only active system windows appear. Implementation is in `InspectorMenuBar.cs` (partial class Inspector). Menu bar uses frosted glass blur (45% opacity). Renders last (always on top). Hover-to-switch between menus when a dropdown is open (macOS behavior). Toggle items keep the dropdown open; action items close it.

**Auto-positioning**: Windows are only repositioned on: game window resize, UI scale change, or Workspace > Reorder Windows. Toggling visibility or creating/destroying windows does NOT reposition.

## GizmosRenderer

```csharp
var gizmos = ECS.GetSystem<GizmosRenderer>();

gizmos.ToggleGizmos();

gizmos.AddGizmoLine(start, end, color, thickness);
gizmos.AddGizmoCircle(center, radius, color);
gizmos.AddGizmoArc(center, radius, startAngle, endAngle, color);
gizmos.AddGizmoRect(rect, color, filled);
gizmos.AddGizmoText(position, text, color);
```

## Scene System

```csharp
public class MyScene : Scene
{
    public override void SetupECS()
    {
        ECS.AddSystem<Inspector>();
        ECS.AddSystem<Geometry>();
        ECS.AddSystem<HRCGI>();
        // ... register systems (order irrelevant, RunAfter/RunBefore handles it)
        base.SetupECS(); // topological sort + Initialize()
    }

    public override void SetupScene()
    {
        int light = LightFactory.CreateLight(ECS, new Vector2(1920, 1080), 50f,
            Color.White, new Color(255, 200, 100, 200));
        base.SetupScene();
    }

    // Optional: Update(), FixedUpdate(), Render(), LateRender() — called before ECS.*
}
```

Properties: `ECS`, `Renderer`, `GameTime`, `DeltaTime`.

## LightFactory

```csharp
LightFactory.CreateLight(ECS, position, radius, albedo, emissive, z?, texture?);
LightFactory.SpawnRandom(ECS, count, screenSize, radius);
LightFactory.HueToRGB(0.5f);
```

## Game Loop

144 FPS render, 64 UPS fixed update. Sleep + spin-wait pacing. Accumulator with 8-iteration cap.

## Controls

F1 = toggle Inspector UI (menu bar + windows). Arrow keys = Pac-Man movement. ESC = close menu dropdown / exit. All other controls via Inspector.

## Not Implemented

Audio, physics engine, particles, skeletal animation, save/load, logging, asset hot-reload.
