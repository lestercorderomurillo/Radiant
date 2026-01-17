using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

/// <summary>
/// Scene Geometry System - Generates geometry buffers for lighting
/// - Emissive: Light sources (color + alpha mask)
/// - Absorption: Surface colors for light interaction
/// - SDF: Signed distance field for raymarching
/// - Future: Normals, velocity, depth, material IDs
/// </summary>
public class SceneGeometry : core.System
{
    private const float SDFScale = 0.5f;

    private Effect SceneGeometryShader;
    private SpriteBatch GeometryBatch;
    
    // Geometry buffers
    private RenderTarget2D EmissiveBuffer;
    private RenderTarget2D AbsorptionBuffer;
    private RenderTarget2D SDFBuffer;
    
    // JFA intermediate textures
    private RenderTarget2D JFATexture1;
    private RenderTarget2D JFATexture2;
    
    // 1x1 white pixel texture for drawing colored rectangles
    private Texture2D PixelTexture;
    
    private Vector2 WorldBounds;
    private Vector2 SDFBounds;
    private float ScreenDiagonal;
    private Rectangle BufferBounds;
    private int JFAPassCount;
    private RenderTarget2D JFAResult;
    
    private bool GeometryDirty;
    private bool UseExternalEmissive;
    
    private int EmissiveCount;
    private int AbsorptionCount;
    
    // Shader parameters
    private EffectParameter ParamEmissiveTexture;
    private EffectParameter ParamJFATexture;
    private EffectParameter ParamWorldsBounds;
    private EffectParameter ParamScreenDiagonal;
    private EffectParameter ParamJumpDistance;
    
    private enum DebugMode { None, Emissive, Absorption, SDF, JFADirection, JFARaw }
    private DebugMode CurrentDebug = DebugMode.None;
    private KeyboardState PrevKeyState;
    private GizmosRenderer Gizmos;

    public override void Initialize()
    {
        base.Initialize();

        SceneGeometryShader = RenderPipeline.Window.Content.Load<Effect>("shaders/SceneGeometry");
        GeometryBatch = new SpriteBatch(RenderPipeline.Device);

        ParamEmissiveTexture = SceneGeometryShader.Parameters["EmissiveTexture"];
        ParamJFATexture = SceneGeometryShader.Parameters["JFATexture"];
        ParamWorldsBounds = SceneGeometryShader.Parameters["WorldsBounds"];
        ParamScreenDiagonal = SceneGeometryShader.Parameters["ScreenDiagonal"];
        ParamJumpDistance = SceneGeometryShader.Parameters["JumpDistance"];

        // Create 1x1 white pixel texture for drawing colored rectangles
        PixelTexture = new Texture2D(RenderPipeline.Device, 1, 1);
        PixelTexture.SetData([Color.White]);

        WorldBounds = new Vector2(
            RenderPipeline.Device.Viewport.Width,
            RenderPipeline.Device.Viewport.Height);

        SDFBounds = new Vector2(
            (int)(WorldBounds.X * SDFScale),
            (int)(WorldBounds.Y * SDFScale));

        ScreenDiagonal = WorldBounds.Length();

        float sdfDiagonal = SDFBounds.Length();
        JFAPassCount = (int)Math.Ceiling(Math.Log(sdfDiagonal, 2)) + 1;

        CreateGeometryBuffers();

        JFAResult = JFATexture1;
        GeometryDirty = true;

        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();
        Gizmos.AddSection("Geometry", "Scene Geometry Buffers", Color.Blue);
        PrevKeyState = Keyboard.GetState();
    }

    private void CreateGeometryBuffers()
    {
        var device = RenderPipeline.Device;

        // Full-resolution buffers
        EmissiveBuffer = new RenderTarget2D(
            device, (int)WorldBounds.X, (int)WorldBounds.Y,
            false, SurfaceFormat.Color, DepthFormat.None);

        AbsorptionBuffer = new RenderTarget2D(
            device, (int)WorldBounds.X, (int)WorldBounds.Y,
            false, SurfaceFormat.Color, DepthFormat.None);

        SDFBuffer = new RenderTarget2D(
            device, (int)WorldBounds.X, (int)WorldBounds.Y,
            false, SurfaceFormat.HalfVector2, DepthFormat.None);

        // Half-resolution JFA textures (performance optimization)
        JFATexture1 = new RenderTarget2D(
            device, (int)SDFBounds.X, (int)SDFBounds.Y,
            false, SurfaceFormat.Vector4, DepthFormat.None);

        JFATexture2 = new RenderTarget2D(
            device, (int)SDFBounds.X, (int)SDFBounds.Y,
            false, SurfaceFormat.Vector4, DepthFormat.None);
    }

    public override void Update()
    {
        HandleInput();

        if (!UseExternalEmissive)
        {
            RenderEmissiveBuffer();
            RenderAbsorptionBuffer();
        }

        if (GeometryDirty)
        {
            RenderSDFBuffer();
            GeometryDirty = false;
        }

        UpdateGizmos();
        UseExternalEmissive = false;
    }

    private void HandleInput()
    {
        var key = Keyboard.GetState();
        if (key.IsKeyDown(Keys.F2) && !PrevKeyState.IsKeyDown(Keys.F2))
        {
            int count = Enum.GetValues<DebugMode>().Length;
            CurrentDebug = (DebugMode)(((int)CurrentDebug + 1) % count);
        }
        PrevKeyState = key;
    }

    public void SetEmissiveFromExternal(Action<SpriteBatch> callback)
    {
        RenderPipeline.Device.SetRenderTarget(EmissiveBuffer);
        RenderPipeline.Device.Clear(Color.Transparent);
        callback.Invoke(GeometryBatch);
        RenderPipeline.Device.SetRenderTarget(null);
        UseExternalEmissive = true;
        GeometryDirty = true;
    }

    private void RenderEmissiveBuffer()
    {
        RenderPipeline.Device.SetRenderTarget(EmissiveBuffer);
        RenderPipeline.Device.Clear(Color.Transparent);

        GeometryBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp);

        int count = 0;

        foreach (var id in Scene.ECS.Query<Transform, Rectangle2D, Material>(culling: false))
        {
            ref var transform = ref Scene.ECS.GetComponent<Transform>(id);
            ref var rect = ref Scene.ECS.GetComponent<Rectangle2D>(id);
            ref var mat = ref Scene.ECS.GetComponent<Material>(id);

            if (mat.Emissive.A == 0) continue;

            Vector2 position = new Vector2(transform.Position.X, transform.Position.Y);

            if (position.X + rect.Size.X >= 0 && position.X < WorldBounds.X &&
                position.Y + rect.Size.Y >= 0 && position.Y < WorldBounds.Y)
            {
                GeometryBatch.Draw(
                    PixelTexture, 
                    position, 
                    new Rectangle(0, 0, 1, 1),
                    mat.Emissive, 
                    0f, 
                    Vector2.Zero, 
                    rect.Size, 
                    SpriteEffects.None, 
                    0f);
                count++;
            }
        }

        GeometryBatch.End();
        RenderPipeline.Device.SetRenderTarget(null);

        EmissiveCount = count;
        GeometryDirty = true;
    }

    private void RenderAbsorptionBuffer()
    {
        RenderPipeline.Device.SetRenderTarget(AbsorptionBuffer);
        RenderPipeline.Device.Clear(Color.Transparent);

        GeometryBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp);

        int count = 0;
        Rectangle screen = new Rectangle(0, 0, (int)WorldBounds.X, (int)WorldBounds.Y);

        foreach (var id in Scene.ECS.Query<Transform, Rectangle2D, Material>(culling: false))
        {
            ref var transform = ref Scene.ECS.GetComponent<Transform>(id);
            ref var rect = ref Scene.ECS.GetComponent<Rectangle2D>(id);
            ref var mat = ref Scene.ECS.GetComponent<Material>(id);

            // Skip fully transparent objects
            if (mat.Albedo.A == 0 && mat.Emissive.A == 0) break;

            BufferBounds.X = (int)transform.Position.X;
            BufferBounds.Y = (int)transform.Position.Y;
            BufferBounds.Width = (int)rect.Size.X;
            BufferBounds.Height = (int)rect.Size.Y;

            if (BufferBounds.Intersects(screen))
            {
                Color absorb = mat.Emissive.A > 0 ? mat.Emissive : mat.Albedo;
                GeometryBatch.Draw(PixelTexture, BufferBounds, absorb);
                count++;
            }
        }

        GeometryBatch.End();
        RenderPipeline.Device.SetRenderTarget(null);

        AbsorptionCount = count;
    }

    private void RenderSDFBuffer()
    {
        InitializeJFA();
        RunJFAPasses();
        GenerateFinalSDF();
    }

    private void InitializeJFA()
    {
        ParamEmissiveTexture?.SetValue(EmissiveBuffer);
        ParamWorldsBounds?.SetValue(SDFBounds);
        ParamScreenDiagonal?.SetValue(ScreenDiagonal);

        RenderPipeline.DrawShader(SceneGeometryShader, "InitializeJFA", JFATexture1, Color.Black);
    }

    private void RunJFAPasses()
    {
        ParamWorldsBounds?.SetValue(SDFBounds);
        ParamScreenDiagonal?.SetValue(ScreenDiagonal);

        JFAResult = RenderPipeline.PingPong(
            SceneGeometryShader, "JFAPass",
            JFATexture1, JFATexture2,
            JFAPassCount,
            beforePass: (pass, input) => {
                int jump = 1 << (JFAPassCount - pass - 1);
                ParamJFATexture?.SetValue(input);
                ParamJumpDistance?.SetValue((float)jump);
            },
            clearColor: Color.Black
        );
    }

    private void GenerateFinalSDF()
    {
        ParamEmissiveTexture?.SetValue(EmissiveBuffer);
        ParamJFATexture?.SetValue(JFAResult);
        ParamWorldsBounds?.SetValue(WorldBounds);
        ParamScreenDiagonal?.SetValue(ScreenDiagonal);

        RenderPipeline.DrawShader(SceneGeometryShader, "GenerateSDFFromJFA", SDFBuffer);
    }

    private void UpdateGizmos()
    {
        Gizmos.ClearSection("Geometry");
        Gizmos.AddSectionString("Geometry", $"Debug: {CurrentDebug} (F2)");
        Gizmos.AddSectionString("Geometry", $"Emissive Objects: {EmissiveCount}");
        Gizmos.AddSectionString("Geometry", $"Absorption Objects: {AbsorptionCount}");
        Gizmos.AddSectionString("Geometry", $"JFA Passes: {JFAPassCount}");
        Gizmos.AddSectionString("Geometry", $"Buffers: {WorldBounds.X}x{WorldBounds.Y}");
        Gizmos.AddSectionString("Geometry", $"SDF: {SDFBounds.X}x{SDFBounds.Y} ({SDFScale:P0})");
    }

    public override void LateRender()
    {
        if (CurrentDebug == DebugMode.None) return;

        switch (CurrentDebug)
        {
            case DebugMode.Emissive:
                ParamEmissiveTexture?.SetValue(EmissiveBuffer);
                ParamWorldsBounds?.SetValue(SDFBounds);
                ParamScreenDiagonal?.SetValue(ScreenDiagonal);
                RenderPipeline.DrawShader(SceneGeometryShader, "DebugEmissive");
                break;
            case DebugMode.Absorption:
                GeometryBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp);
                GeometryBatch.Draw(AbsorptionBuffer, RenderPipeline.Device.Viewport.Bounds, Color.White);
                GeometryBatch.End();
                break;
            case DebugMode.SDF:
                ParamJFATexture?.SetValue(JFAResult);
                ParamEmissiveTexture?.SetValue(EmissiveBuffer);
                ParamWorldsBounds?.SetValue(SDFBounds);
                ParamScreenDiagonal?.SetValue(ScreenDiagonal);
                RenderPipeline.DrawShader(SceneGeometryShader, "DebugSDFVisible");
                break;
            case DebugMode.JFADirection:
                ParamJFATexture?.SetValue(JFAResult);
                ParamWorldsBounds?.SetValue(SDFBounds);
                ParamScreenDiagonal?.SetValue(ScreenDiagonal);
                RenderPipeline.DrawShader(SceneGeometryShader, "DebugJFA");
                break;
            case DebugMode.JFARaw:
                ParamJFATexture?.SetValue(JFAResult);
                ParamWorldsBounds?.SetValue(SDFBounds);
                ParamScreenDiagonal?.SetValue(ScreenDiagonal);
                RenderPipeline.DrawShader(SceneGeometryShader, "DebugJFARaw");
                break;
        }
    }

    // Public accessors for lighting systems
    public RenderTarget2D GetEmissiveTexture() => EmissiveBuffer;
    
    public RenderTarget2D GetAbsorptionTexture() => AbsorptionBuffer;

    public RenderTarget2D GetSDFTexture() => SDFBuffer;

    public float GetSDFScale() => SDFScale;

    public override void Dispose()
    {
        SceneGeometryShader?.Dispose();
        GeometryBatch?.Dispose();
        PixelTexture?.Dispose();
        EmissiveBuffer?.Dispose();
        AbsorptionBuffer?.Dispose();
        SDFBuffer?.Dispose();
        JFATexture1?.Dispose();
        JFATexture2?.Dispose();
    }
}