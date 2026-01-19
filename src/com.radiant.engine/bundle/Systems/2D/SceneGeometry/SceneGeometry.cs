using System;
using com.radiant.engine.core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public class SceneGeometry : core.System
{
    public float SDFScale = 0.25f;

    private RenderTarget2D JFATexture1;
    private RenderTarget2D JFATexture2;

    private Vector2 WorldBounds;
    private Vector2 SDFBounds;
    private float ScreenDiagonal;
    private Rectangle BufferBounds;
    private int JFAPassCount;

    private RenderTarget2D JFAResult;
    public RenderTarget2D EmissiveTexture { get; private set; }
    public RenderTarget2D AbsorptionTexture { get; private set; }
    public RenderTarget2D SDFTexture { get; private set; }

    private int EmissiveCount;
    private int AbsorptionCount;

    private enum DebugMode { None, Emissive, Absorption, SDF, JFADirection, JFARaw }
    private DebugMode CurrentDebug = DebugMode.None;
    private KeyboardState PrevKeyState;
    private GizmosRenderer Gizmos;

    public override void Initialize()
    {
        WorldBounds = Renderer.ScreenSize;

        SDFBounds = new Vector2(
            (int)(WorldBounds.X * SDFScale),
            (int)(WorldBounds.Y * SDFScale));

        ScreenDiagonal = Renderer.ScreenDiagonal;

        float sdfDiagonal = SDFBounds.Length();
        JFAPassCount = (int)Math.Ceiling(Math.Log(sdfDiagonal, 2)) + 1;

        InitializeGeometryBuffers();

        JFAResult = JFATexture1;

        Gizmos = Scene.ECS.GetSystem<GizmosRenderer>();
        PrevKeyState = Keyboard.GetState();
    }

    private void InitializeJFA()
    {
        Renderer
            .Reset()
            .SetShader("SceneGeometry")
            .SetTechnique("InitializeJFA")
            .SetTarget(JFATexture1)
            .Clear(Color.Black)
            .SetParameter("EmissiveTexture", EmissiveTexture)
            .SetParameter("WorldsBounds", SDFBounds)
            .SetParameter("ScreenDiagonal", ScreenDiagonal)
            .Draw()
            .Commit()
            .SetTarget(null);
    }

    private void InitializeGeometryBuffers()
    {
        EmissiveTexture = new RenderTarget2D(
            Renderer.Device, (int)WorldBounds.X, (int)WorldBounds.Y,
            false, SurfaceFormat.Color, DepthFormat.None);

        AbsorptionTexture = new RenderTarget2D(
            Renderer.Device, (int)WorldBounds.X, (int)WorldBounds.Y,
            false, SurfaceFormat.Color, DepthFormat.None);

        SDFTexture = new RenderTarget2D(
            Renderer.Device, (int)WorldBounds.X, (int)WorldBounds.Y,
            false, SurfaceFormat.HalfVector2, DepthFormat.None);

        JFATexture1 = new RenderTarget2D(
            Renderer.Device, (int)SDFBounds.X, (int)SDFBounds.Y,
            false, SurfaceFormat.Vector4, DepthFormat.None);

        JFATexture2 = new RenderTarget2D(
            Renderer.Device, (int)SDFBounds.X, (int)SDFBounds.Y,
            false, SurfaceFormat.Vector4, DepthFormat.None);
    }

    public override void Update()
    {
        // Handle input
        var key = Keyboard.GetState();

        if (key.IsKeyDown(Keys.F2) && !PrevKeyState.IsKeyDown(Keys.F2))
        {
            int count = Enum.GetValues<DebugMode>().Length;
            CurrentDebug = (DebugMode)(((int)CurrentDebug + 1) % count);
        }

        PrevKeyState = key;

        RenderEmissiveTexture();
        RenderAbsorptionTexture();
        RenderSDFTexture();

        UpdateGizmos();
    }
    
    private void RenderEmissiveTexture()
    {
        Renderer
            .Reset()
            .Configure(BlendState.AlphaBlend)
            .Configure(SamplerState.PointClamp)
            .SetTarget(EmissiveTexture)
            .Clear(Color.Transparent);

        int count = 0;

        // Render rectangles
        foreach (var id in Scene.ECS.Query<Transform, Rectangle2D, Material>(culling: false))
        {
            ref var transform = ref Scene.ECS.GetComponent<Transform>(id);
            ref var rect = ref Scene.ECS.GetComponent<Rectangle2D>(id);
            ref var mat = ref Scene.ECS.GetComponent<Material>(id);

            if (mat.Emissive.A == 0) continue;

            Vector2 position = new Vector2(MathF.Round(transform.Position.X), MathF.Round(transform.Position.Y));

            if (position.X + rect.Size.X >= 0 && position.X < WorldBounds.X &&
                position.Y + rect.Size.Y >= 0 && position.Y < WorldBounds.Y)
            {
                Renderer.DrawTexture(
                    Renderer.GetSolidTexture(Color.White),
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

        // Render circles
        foreach (var id in Scene.ECS.Query<Transform, Circle2D, Material>(culling: false))
        {
            ref var transform = ref Scene.ECS.GetComponent<Transform>(id);
            ref var circle = ref Scene.ECS.GetComponent<Circle2D>(id);
            ref var mat = ref Scene.ECS.GetComponent<Material>(id);

            if (mat.Emissive.A == 0) continue;

            float diameter = circle.Radius * 2;
            Vector2 position = new Vector2(MathF.Round(transform.Position.X - circle.Radius), MathF.Round(transform.Position.Y - circle.Radius));

            if (position.X + diameter >= 0 && position.X < WorldBounds.X &&
                position.Y + diameter >= 0 && position.Y < WorldBounds.Y)
            {
                int texDiameter = Math.Max(1, (int)diameter);
                var circleTexture = Renderer.GetCircleTexture(texDiameter);

                Renderer.DrawTexture(
                    circleTexture,
                    position,
                    null,
                    mat.Emissive,
                    0f,
                    Vector2.Zero,
                    Vector2.One,
                    SpriteEffects.None,
                    0f);
                count++;
            }
        }

        Renderer
            .Commit()
            .SetTarget(null);

        EmissiveCount = count;
    }

    private void RenderAbsorptionTexture()
    {
        Renderer
            .Reset()
            .Configure(BlendState.AlphaBlend)
            .Configure(SamplerState.PointClamp)
            .SetTarget(AbsorptionTexture)
            .Clear(Color.Transparent);

        int count = 0;
        Rectangle screen = new Rectangle(0, 0, (int)WorldBounds.X, (int)WorldBounds.Y);

        // Render rectangles
        foreach (var id in Scene.ECS.Query<Transform, Rectangle2D, Material>(culling: false))
        {
            ref var transform = ref Scene.ECS.GetComponent<Transform>(id);
            ref var rect = ref Scene.ECS.GetComponent<Rectangle2D>(id);
            ref var mat = ref Scene.ECS.GetComponent<Material>(id);

            if (mat.Albedo.A == 0) continue;

            BufferBounds.X = (int)MathF.Round(transform.Position.X);
            BufferBounds.Y = (int)MathF.Round(transform.Position.Y);
            BufferBounds.Width = (int)rect.Size.X;
            BufferBounds.Height = (int)rect.Size.Y;

            if (BufferBounds.Intersects(screen))
            {
                Renderer.DrawTexture(Renderer.GetSolidTexture(Color.White), BufferBounds, mat.Albedo);
                count++;
            }
        }

        // Render circles
        foreach (var id in Scene.ECS.Query<Transform, Circle2D, Material>(culling: false))
        {
            ref var transform = ref Scene.ECS.GetComponent<Transform>(id);
            ref var circle = ref Scene.ECS.GetComponent<Circle2D>(id);
            ref var mat = ref Scene.ECS.GetComponent<Material>(id);

            if (mat.Albedo.A == 0) continue;

            int diameter = Math.Max(1, (int)(circle.Radius * 2));
            int x = (int)MathF.Round(transform.Position.X - circle.Radius);
            int y = (int)MathF.Round(transform.Position.Y - circle.Radius);

            BufferBounds.X = x;
            BufferBounds.Y = y;
            BufferBounds.Width = diameter;
            BufferBounds.Height = diameter;

            if (BufferBounds.Intersects(screen))
            {
                var circleTexture = Renderer.GetCircleTexture(diameter);
                Renderer.DrawTexture(circleTexture, new Rectangle(x, y, diameter, diameter), mat.Albedo);
                count++;
            }
        }

        Renderer
            .Commit()
            .SetTarget(null);

        AbsorptionCount = count;
    }

    private void RenderSDFTexture()
    {
        InitializeJFA();
        RunJFAPasses();
        GenerateFinalSDF();
    }

    private void RunJFAPasses()
    {
        Renderer
            .Reset()
            .SetShader("SceneGeometry")
            .SetTechnique("JFAPass")
            .SetParameter("WorldsBounds", SDFBounds)
            .SetParameter("ScreenDiagonal", ScreenDiagonal);

        JFAResult = Renderer.PingPong(
            JFATexture1, JFATexture2,
            JFAPassCount,
            beforePass: (pass, input) =>
            {
                int jump = 1 << (JFAPassCount - pass - 1);
                Renderer
                    .SetParameter("JFATexture", input)
                    .SetParameter("JumpDistance", (float)jump);
            },
            clearColor: Color.Black
        );
    }

    private void GenerateFinalSDF()
    {
        Renderer
            .Reset()
            .SetShader("SceneGeometry")
            .SetTechnique("GenerateSDFFromJFA")
            .SetTarget(SDFTexture)
            .SetParameter("EmissiveTexture", EmissiveTexture)
            .SetParameter("JFATexture", JFAResult)
            .SetParameter("WorldsBounds", WorldBounds)
            .SetParameter("ScreenDiagonal", ScreenDiagonal)
            .Draw()
            .Commit()
            .SetTarget(null);
    }

    private void UpdateGizmos()
    {
        Gizmos.Set("Geometry", $"Debug: {CurrentDebug} (F2)");
        Gizmos.Set("Geometry", $"Emissive Objects: {EmissiveCount}");
        Gizmos.Set("Geometry", $"Absorption Objects: {AbsorptionCount}");
        Gizmos.Set("Geometry", $"JFA Passes: {JFAPassCount}");
        Gizmos.Set("Geometry", $"Buffers: {WorldBounds.X}x{WorldBounds.Y}");
        Gizmos.Set("Geometry", $"SDF: {SDFBounds.X}x{SDFBounds.Y} ({SDFScale:P0})");
    }

    public override void LateRender()
    {
        if (CurrentDebug == DebugMode.None) return;

        switch (CurrentDebug)
        {
            case DebugMode.Emissive:
                Renderer
                    .Reset()
                    .SetShader("SceneGeometry")
                    .SetTechnique("DebugEmissive")
                    .SetTarget(null)
                    .SetParameter("EmissiveTexture", EmissiveTexture)
                    .SetParameter("WorldsBounds", SDFBounds)
                    .SetParameter("ScreenDiagonal", ScreenDiagonal)
                    .Draw()
                    .Commit();
                break;

            case DebugMode.Absorption:
                Renderer
                    .Reset()
                    .Configure(BlendState.AlphaBlend)
                    .Configure(SamplerState.LinearClamp)
                    .SetTarget(null)
                    .DrawTexture(AbsorptionTexture, new Rectangle(0, 0, Renderer.ScreenWidth, Renderer.ScreenHeight), Color.White)
                    .Commit();
                break;

            case DebugMode.SDF:
                Renderer
                    .Reset()
                    .SetShader("SceneGeometry")
                    .SetTechnique("DebugSDFVisible")
                    .SetTarget(null)
                    .SetParameter("JFATexture", JFAResult)
                    .SetParameter("EmissiveTexture", EmissiveTexture)
                    .SetParameter("WorldsBounds", SDFBounds)
                    .SetParameter("ScreenDiagonal", ScreenDiagonal)
                    .Draw()
                    .Commit();
                break;

            case DebugMode.JFADirection:
                Renderer
                    .Reset()
                    .SetShader("SceneGeometry")
                    .SetTechnique("DebugJFA")
                    .SetTarget(null)
                    .SetParameter("JFATexture", JFAResult)
                    .SetParameter("WorldsBounds", SDFBounds)
                    .SetParameter("ScreenDiagonal", ScreenDiagonal)
                    .Draw()
                    .Commit();
                break;

            case DebugMode.JFARaw:
                Renderer
                    .Reset()
                    .SetShader("SceneGeometry")
                    .SetTechnique("DebugJFARaw")
                    .SetTarget(null)
                    .SetParameter("JFATexture", JFAResult)
                    .SetParameter("WorldsBounds", SDFBounds)
                    .SetParameter("ScreenDiagonal", ScreenDiagonal)
                    .Draw()
                    .Commit();
                break;
        }
    }

    public override void Dispose()
    {
        EmissiveTexture?.Dispose();
        AbsorptionTexture?.Dispose();
        SDFTexture?.Dispose();
        JFATexture1?.Dispose();
        JFATexture2?.Dispose();
    }
}
