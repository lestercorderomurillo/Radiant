using com.radiant.engine.bundle;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace com.radiant.engine.core;

public class MegaLightsScene : Scene
{
    private int MouseLightId;
    private int[] RotatingLightIds;
    private int[] OccluderIds;

    private MouseState PrevMouse;
    private KeyboardState PrevKeyboard;
    private Random Rng = new();

    private float Rotation;
    private bool IsAnimating = true;

    private HRCGI HRCGISystem;
    private RCGI RCGISystem;
    private UDR1 UDR1System;
    private UDR2 UDR2System;
    private Geometry GeometrySystem;
    private GizmosRenderer Gizmos;

    private bool UseHRCGI = true;
    private bool UseUDR2 = false;  // Toggle between UDR1 and UDR2

    private const float RotationSpeed = 0.1f;
    private const float OrbitRadius = 300f;
    private const int LightCount = 12;
    private const int OccluderCount = 80;
    private const float OccluderMargin = 150f;
    private const float CenterClearance = 400f;

    public override void SetupECS()
    {
        ECS.AddSystem<PerformanceMonitor>();
        GeometrySystem = ECS.AddSystem<Geometry>();
        HRCGISystem = ECS.AddSystem<HRCGI>();
        RCGISystem = ECS.AddSystem<RCGI>(enabled: false);
        UDR1System = ECS.AddSystem<UDR1>();
        UDR2System = ECS.AddSystem<UDR2>(enabled: false);
        Gizmos = ECS.AddSystem<GizmosRenderer>();

        GeometrySystem.EnableSDF = false;

        base.SetupECS();
    }

    public override void SetupScene()
    {
        CreateRotatingLights();
        CreateOccluders();
        CreateMouseLight();
        UpdateUDRInput();

        base.SetupScene();
    }

    private void CreateRotatingLights()
    {
        RotatingLightIds = new int[LightCount];
        var center = Renderer.Window.GetScreenCenter();

        for (int i = 0; i < LightCount; i++)
        {
            float angle = i / (float)LightCount * MathHelper.TwoPi;
            float x = center.X + OrbitRadius * MathF.Cos(angle);
            float y = center.Y + OrbitRadius * MathF.Sin(angle);

            // Rainbow colors
            float hue = i / (float)LightCount;
            var color = HueToRGB(hue);

            RotatingLightIds[i] = CreateLight(new Vector2(x, y), 40f, color);
        }
    }

    private void CreateOccluders()
    {
        OccluderIds = new int[OccluderCount];
        var screen = Renderer.Window.GetScreenSize();
        var center = Renderer.Window.GetScreenCenter();

        for (int i = 0; i < OccluderCount; i++)
        {
            Vector2 pos;
            do
            {
                pos = new Vector2(
                    OccluderMargin + (float)Rng.NextDouble() * (screen.X - 2 * OccluderMargin),
                    OccluderMargin + (float)Rng.NextDouble() * (screen.Y - 2 * OccluderMargin)
                );
            } while (Vector2.Distance(pos, center) < CenterClearance);

            OccluderIds[i] = CreateOccluder(pos, 60f);
        }
    }

    private void CreateMouseLight()
    {
        var mouse = Mouse.GetState();
        MouseLightId = CreateLight(new Vector2(mouse.X, mouse.Y), 50f, Color.White);
        PrevMouse = mouse;
    }

    private int CreateLight(Vector2 position, float radius, Color color)
    {
        int id = ECS.CreateEntity();

        ref var transform = ref ECS.AddComponent<Transform>(id);
        ref var circle = ref ECS.AddComponent<Circle2D>(id);
        ref var material = ref ECS.AddComponent<Material>(id);

        transform.Position = new Vector3(position, 0);
        transform.Rotation = Vector3.UnitX;
        circle.Radius = radius;
        material.Albedo = color;
        material.Emissive = color;

        return id;
    }

    private int CreateOccluder(Vector2 position, float size)
    {
        int id = ECS.CreateEntity();

        ref var transform = ref ECS.AddComponent<Transform>(id);
        ref var rect = ref ECS.AddComponent<Rectangle2D>(id);
        ref var material = ref ECS.AddComponent<Material>(id);

        transform.Position = new Vector3(position, 0);
        transform.Rotation = Vector3.UnitX;
        rect.Size = new Vector2(size);
        material.Albedo = new Color(30, 30, 30);
        material.Emissive = Color.Black;

        return id;
    }

    public override void Update()
    {
        var keyboard = Keyboard.GetState();
        var mouse = Mouse.GetState();
        var center = Renderer.Window.GetScreenCenter();

        // Space: toggle animation
        if (keyboard.IsKeyDown(Keys.Space) && PrevKeyboard.IsKeyUp(Keys.Space))
            IsAnimating = !IsAnimating;

        // Tab: toggle GI system
        if (keyboard.IsKeyDown(Keys.Tab) && PrevKeyboard.IsKeyUp(Keys.Tab))
            ToggleGISystem();

        // Animate rotating lights
        if (IsAnimating)
            Rotation += RotationSpeed * DeltaTime;

        for (int i = 0; i < RotatingLightIds.Length; i++)
        {
            float angle = i / (float)LightCount * MathHelper.TwoPi + Rotation;
            float x = center.X + OrbitRadius * MathF.Cos(angle);
            float y = center.Y + OrbitRadius * MathF.Sin(angle);

            ref var transform = ref ECS.GetComponent<Transform>(RotatingLightIds[i]);
            transform.Position = new Vector3(x, y, 0);
        }

        // Update mouse light
        ref var mouseTransform = ref ECS.GetComponent<Transform>(MouseLightId);
        mouseTransform.Position = new Vector3(mouse.X, mouse.Y, 0);

        // Only allow spawning when window is focused
        if (Renderer.Window.IsActive)
        {
            // Left click: spawn light
            if (mouse.LeftButton == ButtonState.Pressed && PrevMouse.LeftButton == ButtonState.Released)
            {
                var color = HueToRGB((float)Rng.NextDouble());
                CreateLight(new Vector2(mouse.X, mouse.Y), 40f, color);
            }

            // Right click: spawn occluder
            if (mouse.RightButton == ButtonState.Pressed && PrevMouse.RightButton == ButtonState.Released)
                CreateOccluder(new Vector2(mouse.X, mouse.Y), 40f);
        }

        // F11 to toggle UDR mode
        if (keyboard.IsKeyDown(Keys.F11) && PrevKeyboard.IsKeyUp(Keys.F11))
            ToggleUDRSystem();

        PrevKeyboard = keyboard;
        PrevMouse = mouse;

        Gizmos.Set("Scene", $"GI: {(UseHRCGI ? "HRCGI" : "RCGI")} [Tab] | Animation: {(IsAnimating ? "On" : "Off")} [Space]");
        Gizmos.Set("Scene", $"Upscaler: {(UseUDR2 ? "UDR2 (Temporal)" : "UDR1 (Spatial)")} [F11]");
        Gizmos.Set("Scene", "Left Click: Add Light | Right Click: Add Occluder");
    }

    private void ToggleGISystem()
    {
        UseHRCGI = !UseHRCGI;

        if (UseHRCGI)
        {
            RCGISystem.Dispose();
            RCGISystem.Enabled = false;
            HRCGISystem.Initialize();
            HRCGISystem.Enabled = true;
            GeometrySystem.EnableSDF = false;
        }
        else
        {
            HRCGISystem.Dispose();
            HRCGISystem.Enabled = false;
            RCGISystem.Initialize();
            RCGISystem.Enabled = true;
            GeometrySystem.EnableSDF = true;
        }

        UpdateUDRInput();
    }

    private void UpdateUDRInput()
    {
        var inputSource = new Func<Texture2D>(() => UseHRCGI ? HRCGISystem.GetOutput() : RCGISystem.GetOutput());
        UDR1System.SetInputSource(inputSource);
        UDR2System.SetInputSource(inputSource);
    }

    private void ToggleUDRSystem()
    {
        UseUDR2 = !UseUDR2;

        if (UseUDR2)
        {
            UDR1System.Dispose();
            UDR1System.Enabled = false;
            UDR2System.Initialize();
            UDR2System.Enabled = true;
        }
        else
        {
            UDR2System.Dispose();
            UDR2System.Enabled = false;
            UDR1System.Initialize();
            UDR1System.Enabled = true;
        }

        UpdateUDRInput();
    }

    private static Color HueToRGB(float hue)
    {
        float r = MathF.Abs(hue * 6f - 3f) - 1f;
        float g = 2f - MathF.Abs(hue * 6f - 2f);
        float b = 2f - MathF.Abs(hue * 6f - 4f);
        return new Color(
            (byte)(Math.Clamp(r, 0f, 1f) * 255),
            (byte)(Math.Clamp(g, 0f, 1f) * 255),
            (byte)(Math.Clamp(b, 0f, 1f) * 255)
        );
    }
}
