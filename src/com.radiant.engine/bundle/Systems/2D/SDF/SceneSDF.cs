using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public class SceneSDF : core.System
{
    private const float SDFScale = 0.5f;

    private Effect SDFShader;
    private SpriteBatch ShaderSpriteBatch;
    
    private RenderTarget2D EmissiveTexture;
    private RenderTarget2D AbsorptionTexture;
    private RenderTarget2D JFATexture1;
    private RenderTarget2D JFATexture2;
    private RenderTarget2D SDFTexture;
    
    private Texture2D PixelTexture;
    
    private Vector2 WorldBounds;
    private Vector2 SDFBounds;
    private float ScreenDiagonal;
    private Rectangle BufferBounds;
    private int CachedPassCount;
    private RenderTarget2D JFAResult;
    
    private bool SDFDirty;
    private bool UseExternalEmissive;
    
    private int EmissiveCount;
    private int AbsorptionCount;
    
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

        SDFShader = RenderPipeline.Window.Content.Load<Effect>("shaders/SDF");
        ShaderSpriteBatch = new SpriteBatch(RenderPipeline.GraphicsDevice);

        ParamEmissiveTexture = SDFShader.Parameters["EmissiveTexture"];
        ParamJFATexture = SDFShader.Parameters["JFATexture"];
        ParamWorldsBounds = SDFShader.Parameters["WorldsBounds"];
        ParamScreenDiagonal = SDFShader.Parameters["ScreenDiagonal"];
        ParamJumpDistance = SDFShader.Parameters["JumpDistance"];

        PixelTexture = new Texture2D(RenderPipeline.GraphicsDevice, 1, 1);
        PixelTexture.SetData(new[] { Color.White });

        WorldBounds = new Vector2(
            RenderPipeline.GraphicsDevice.Viewport.Width,
            RenderPipeline.GraphicsDevice.Viewport.Height);

        SDFBounds = new Vector2(
            (int)(WorldBounds.X * SDFScale),
            (int)(WorldBounds.Y * SDFScale));

        ScreenDiagonal = WorldBounds.Length();

        float sdfDiagonal = SDFBounds.Length();
        CachedPassCount = (int)Math.Ceiling(Math.Log(sdfDiagonal, 2)) + 1;

        CreateRenderTargets();

        JFAResult = JFATexture1;
        SDFDirty = true;

        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();
        Gizmos.AddSection("SDF", "Scene SDF System", Color.Blue);
        PrevKeyState = Keyboard.GetState();
    }

    private void CreateRenderTargets()
    {
        var device = RenderPipeline.GraphicsDevice;

        EmissiveTexture = new RenderTarget2D(
            device, (int)WorldBounds.X, (int)WorldBounds.Y,
            false, SurfaceFormat.Color, DepthFormat.None);

        AbsorptionTexture = new RenderTarget2D(
            device, (int)WorldBounds.X, (int)WorldBounds.Y,
            false, SurfaceFormat.Color, DepthFormat.None);

        JFATexture1 = new RenderTarget2D(
            device, (int)SDFBounds.X, (int)SDFBounds.Y,
            false, SurfaceFormat.Vector4, DepthFormat.None);

        JFATexture2 = new RenderTarget2D(
            device, (int)SDFBounds.X, (int)SDFBounds.Y,
            false, SurfaceFormat.Vector4, DepthFormat.None);

        SDFTexture = new RenderTarget2D(
            device, (int)WorldBounds.X, (int)WorldBounds.Y,
            false, SurfaceFormat.HalfVector2, DepthFormat.None);
    }

    public override void Update()
    {
        HandleInput();

        if (!UseExternalEmissive)
        {
            UpdateEmissiveTexture();
            UpdateAbsorptionTexture();
        }

        if (SDFDirty)
        {
            UpdateSDFTexture();
            SDFDirty = false;
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

    public void UpdateEmissiveTextureFromExternal(Action<SpriteBatch> callback)
    {
        RenderPipeline.GraphicsDevice.SetRenderTarget(EmissiveTexture);
        RenderPipeline.GraphicsDevice.Clear(Color.Transparent);
        callback.Invoke(ShaderSpriteBatch);
        RenderPipeline.GraphicsDevice.SetRenderTarget(null);
        UseExternalEmissive = true;
        SDFDirty = true;
    }

    private void UpdateEmissiveTexture()
    {
        RenderPipeline.GraphicsDevice.SetRenderTarget(EmissiveTexture);
        RenderPipeline.GraphicsDevice.Clear(Color.Transparent);

        ShaderSpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp);

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
                ShaderSpriteBatch.Draw(
                    PixelTexture, position, new Rectangle(0, 0, 1, 1),
                    mat.Emissive, 0f, Vector2.Zero, rect.Size, SpriteEffects.None, 0f);
                count++;
            }
        }

        ShaderSpriteBatch.End();
        RenderPipeline.GraphicsDevice.SetRenderTarget(null);

        EmissiveCount = count;
        SDFDirty = true;
    }

    private void UpdateAbsorptionTexture()
    {
        RenderPipeline.GraphicsDevice.SetRenderTarget(AbsorptionTexture);
        RenderPipeline.GraphicsDevice.Clear(Color.Transparent);

        ShaderSpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp);

        int count = 0;
        Rectangle screen = new Rectangle(0, 0, (int)WorldBounds.X, (int)WorldBounds.Y);

        foreach (var id in Scene.ECS.Query<Transform, Rectangle2D, Material>(culling: false))
        {
            ref var transform = ref Scene.ECS.GetComponent<Transform>(id);
            ref var rect = ref Scene.ECS.GetComponent<Rectangle2D>(id);
            ref var mat = ref Scene.ECS.GetComponent<Material>(id);

            BufferBounds.X = (int)transform.Position.X;
            BufferBounds.Y = (int)transform.Position.Y;
            BufferBounds.Width = (int)rect.Size.X;
            BufferBounds.Height = (int)rect.Size.Y;

            if (BufferBounds.Intersects(screen))
            {
                Color absorb = mat.Emissive.A > 0 ? mat.Emissive : mat.Albedo;
                ShaderSpriteBatch.Draw(PixelTexture, BufferBounds, absorb);
                count++;
            }
        }

        ShaderSpriteBatch.End();
        RenderPipeline.GraphicsDevice.SetRenderTarget(null);

        AbsorptionCount = count;
    }

    private void UpdateSDFTexture()
    {
        InitializeJFA();
        RunJFAPasses();
        GenerateFinalSDF();
    }

    private void InitializeJFA()
    {
        ParamEmissiveTexture?.SetValue(EmissiveTexture);
        ParamWorldsBounds?.SetValue(SDFBounds);
        ParamScreenDiagonal?.SetValue(ScreenDiagonal);

        RenderPipeline.DrawShader(SDFShader, "InitializeJFA", JFATexture1, Color.Black);
    }

    private void RunJFAPasses()
    {
        ParamWorldsBounds?.SetValue(SDFBounds);
        ParamScreenDiagonal?.SetValue(ScreenDiagonal);

        JFAResult = RenderPipeline.PingPong(
            SDFShader, "JFAPass",
            JFATexture1, JFATexture2,
            CachedPassCount,
            beforePass: (pass, input) => {
                int jump = 1 << (CachedPassCount - pass - 1);
                ParamJFATexture?.SetValue(input);
                ParamJumpDistance?.SetValue((float)jump);
            },
            clearColor: Color.Black
        );
    }

    private void GenerateFinalSDF()
    {
        ParamEmissiveTexture?.SetValue(EmissiveTexture);
        ParamJFATexture?.SetValue(JFAResult);
        ParamWorldsBounds?.SetValue(WorldBounds);
        ParamScreenDiagonal?.SetValue(ScreenDiagonal);

        RenderPipeline.DrawShader(SDFShader, "GenerateSDFFromJFA", SDFTexture);
    }

    private void UpdateGizmos()
    {
        Gizmos.ClearSection("SDF");
        Gizmos.AddSectionString("SDF", $"Debug: {CurrentDebug} (F2)");
        Gizmos.AddSectionString("SDF", $"Emissive: {EmissiveCount}");
        Gizmos.AddSectionString("SDF", $"Absorption: {AbsorptionCount}");
        Gizmos.AddSectionString("SDF", $"JFA Passes: {CachedPassCount}");
        Gizmos.AddSectionString("SDF", $"Output: {WorldBounds.X}x{WorldBounds.Y} (full res)");
        Gizmos.AddSectionString("SDF", $"JFA: {SDFBounds.X}x{SDFBounds.Y} ({SDFScale:P0})");
        Gizmos.AddSectionString("SDF", $"Screen Diagonal: {ScreenDiagonal:F0}px");
    }

    public override void LateRender()
    {
        if (CurrentDebug == DebugMode.None) return;

        switch (CurrentDebug)
        {
            case DebugMode.Emissive:
                ParamEmissiveTexture?.SetValue(EmissiveTexture);
                ParamWorldsBounds?.SetValue(SDFBounds);
                ParamScreenDiagonal?.SetValue(ScreenDiagonal);
                RenderPipeline.DrawShader(SDFShader, "DebugEmissive");
                break;
            case DebugMode.Absorption:
                ShaderSpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp);
                ShaderSpriteBatch.Draw(AbsorptionTexture, RenderPipeline.GraphicsDevice.Viewport.Bounds, Color.White);
                ShaderSpriteBatch.End();
                break;
            case DebugMode.SDF:
                ParamJFATexture?.SetValue(JFAResult);
                ParamEmissiveTexture?.SetValue(EmissiveTexture);
                ParamWorldsBounds?.SetValue(SDFBounds);
                ParamScreenDiagonal?.SetValue(ScreenDiagonal);
                RenderPipeline.DrawShader(SDFShader, "DebugSDFVisible");
                break;
            case DebugMode.JFADirection:
                ParamJFATexture?.SetValue(JFAResult);
                ParamWorldsBounds?.SetValue(SDFBounds);
                ParamScreenDiagonal?.SetValue(ScreenDiagonal);
                RenderPipeline.DrawShader(SDFShader, "DebugJFA");
                break;
            case DebugMode.JFARaw:
                ParamJFATexture?.SetValue(JFAResult);
                ParamWorldsBounds?.SetValue(SDFBounds);
                ParamScreenDiagonal?.SetValue(ScreenDiagonal);
                RenderPipeline.DrawShader(SDFShader, "DebugJFARaw");
                break;
        }
    }

    public RenderTarget2D GetEmissiveTexture() => EmissiveTexture;
    public RenderTarget2D GetAbsorptionTexture() => AbsorptionTexture;
    public RenderTarget2D GetSDFTexture() => SDFTexture;
    public float GetSDFScale() => SDFScale;

    public override void Dispose()
    {
        SDFShader?.Dispose();
        PixelTexture?.Dispose();
        ShaderSpriteBatch?.Dispose();
        EmissiveTexture?.Dispose();
        AbsorptionTexture?.Dispose();
        SDFTexture?.Dispose();
        JFATexture1?.Dispose();
        JFATexture2?.Dispose();
    }
}