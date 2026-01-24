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
    private float RainbowHue = 0f;
    private const float HueSpeed = 0.008f;
    private const float PaintRadius = 8f;
    private const float PaintSpacing = 3f; // Spacing between painted lights (half radius for overlap)
    private Vector2 LastPaintPos;
    private bool HasLastPaintPos = false;
    private bool IsAnimating = true;

    private HRCGI HRCGISystem;
    private RCGI RCGISystem;
    private UDR1 UDR1System;
    private UDR2 UDR2System;
    private UDR3 UDR3System;
    private Geometry Geometry;
    private GizmosRenderer Gizmos;

    private bool UseHRCGI = true;
    private int UDRMode = 2;  // 0 = UDR1, 1 = UDR2, 2 = UDR3

    private const float RotationSpeed = 0.15f;
    private const float OrbitRadius = 300f;
    private const int LightCount = 12;
    private const int OccluderCount = 80;
    private const float OccluderMargin = 150f;
    private const float CenterClearance = 400f;

    public override void SetupECS()
    {
        ECS.AddSystem<PerformanceMonitor>();
        Geometry = ECS.AddSystem<Geometry>();
        HRCGISystem = ECS.AddSystem<HRCGI>();
        RCGISystem = ECS.AddSystem<RCGI>(enabled: false);
        UDR1System = ECS.AddSystem<UDR1>(enabled: false);
        UDR2System = ECS.AddSystem<UDR2>(enabled: false);
        UDR3System = ECS.AddSystem<UDR3>();
        Gizmos = ECS.AddSystem<GizmosRenderer>();

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
            var mousePos = new Vector2(mouse.X, mouse.Y);

            // Left click + moving: spawn lights continuously with perfect rainbow gradient
            if (mouse.LeftButton == ButtonState.Pressed)
            {
                if (!HasLastPaintPos)
                {
                    // First paint point
                    PaintLightAt(mousePos);
                    LastPaintPos = mousePos;
                    HasLastPaintPos = true;
                }
                else
                {
                    // Interpolate between last position and current to fill gaps
                    float distance = Vector2.Distance(LastPaintPos, mousePos);
                    if (distance >= PaintSpacing)
                    {
                        Vector2 direction = Vector2.Normalize(mousePos - LastPaintPos);
                        float traveled = PaintSpacing;

                        while (traveled <= distance)
                        {
                            Vector2 paintPos = LastPaintPos + direction * traveled;
                            PaintLightAt(paintPos);
                            traveled += PaintSpacing;
                        }

                        LastPaintPos = mousePos;
                    }
                }
            }
            else
            {
                HasLastPaintPos = false;
            }

            // Right click: spawn occluder
            if (mouse.RightButton == ButtonState.Pressed && PrevMouse.RightButton == ButtonState.Released)
                CreateOccluder(new Vector2(mouse.X, mouse.Y), 40f);
        }

        // F11 to toggle UDR mode
        if (keyboard.IsKeyDown(Keys.F11) && PrevKeyboard.IsKeyUp(Keys.F11))
            ToggleUDRSystem();

        // X to spawn 10,000 random debug entities
        if (keyboard.IsKeyDown(Keys.X) && PrevKeyboard.IsKeyUp(Keys.X))
            SpawnDebugEntities(25000);

        PrevKeyboard = keyboard;
        PrevMouse = mouse;

        Gizmos.Set("Scene", $"GI: {(UseHRCGI ? "HRCGI" : "RCGI")} [Tab] | Animation: {(IsAnimating ? "On" : "Off")} [Space]");
        Gizmos.Set("Scene", $"Upscaler: {GetUDRName()} [F11]");
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
        }
        else
        {
            HRCGISystem.Dispose();
            HRCGISystem.Enabled = false;
            RCGISystem.Initialize();
            RCGISystem.Enabled = true;
        }

        UpdateUDRInput();
    }

    private void UpdateUDRInput()
    {
        var inputSource = new Func<Texture2D>(() => UseHRCGI ? HRCGISystem.GetOutput() : RCGISystem.GetOutput());
        UDR1System.SetInputSource(inputSource);
        UDR2System.SetInputSource(inputSource);
        UDR3System.SetInputSource(inputSource);
    }

    private void ToggleUDRSystem()
    {
        // Disable current UDR system
        switch (UDRMode)
        {
            case 0:
                UDR1System.Dispose();
                UDR1System.Enabled = false;
                break;
            case 1:
                UDR2System.Dispose();
                UDR2System.Enabled = false;
                break;
            case 2:
                UDR3System.Dispose();
                UDR3System.Enabled = false;
                break;
        }

        // Cycle to next mode
        UDRMode = (UDRMode + 1) % 3;

        // Enable new UDR system
        switch (UDRMode)
        {
            case 0:
                UDR1System.Initialize();
                UDR1System.Enabled = true;
                break;
            case 1:
                UDR2System.Initialize();
                UDR2System.Enabled = true;
                break;
            case 2:
                UDR3System.Initialize();
                UDR3System.Enabled = true;
                break;
        }

        UpdateUDRInput();
    }

    private string GetUDRName()
    {
        return UDRMode switch
        {
            0 => "UDR1 (Spatial)",
            1 => "UDR2 (Spatial + Temporal)",
            2 => "UDR3 (Bilinear)",
            _ => "Unknown"
        };
    }

    private void PaintLightAt(Vector2 position)
    {
        var color = HueToRGB(RainbowHue);
        RainbowHue = (RainbowHue + HueSpeed) % 1f;

        // Check if there's an existing light at this position (ECS spatial query)
        var nearby = ECS.InRadius(new Vector3(position, 0), PaintRadius);
        foreach (int entityId in nearby)
        {
            // Only replace if it has Circle2D (is a light, not an occluder)
            if (ECS.HasComponent<Circle2D>(entityId) && entityId != MouseLightId)
            {
                // Replace existing light's color
                ref var material = ref ECS.GetComponent<Material>(entityId);
                material.Albedo = color;
                material.Emissive = color;
                return;
            }
        }

        // No overlap, create new light
        CreateLight(position, PaintRadius, color);
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

    private void SpawnDebugEntities(int count)
    {
        var screen = Renderer.Window.GetScreenSize();

        for (int i = 0; i < count; i++)
        {
            float x = (float)Rng.NextDouble() * screen.X;
            float y = (float)Rng.NextDouble() * screen.Y;
            var color = HueToRGB((float)Rng.NextDouble());

            CreateLight(new Vector2(x, y), 3f, color);
        }
    }
}
