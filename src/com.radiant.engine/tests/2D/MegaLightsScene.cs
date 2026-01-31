using com.radiant.engine.bundle;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

namespace com.radiant.engine.core;

public class MegaLightsScene : Scene
{
    private int MouseLightId;
    private int[] RotatingLightIds;

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
    private Vector2 LastRightPaintPos;
    private bool HasLastRightPaintPos = false;
    private Vector2 LastMiddlePaintPos;
    private bool HasLastMiddlePaintPos = false;
    private bool IsAnimating = true;

    private HRCGI HRCGISystem;
    private RCGI RCGISystem;
    private Bilinear BilinearSystem;
    private UDR1 UDR1System;
    private UDR2 UDR2System;
    private UDR3 UDR3System;
    private GizmosRenderer Gizmos;

    private bool UseHRCGI = true;
    private int UDRMode = 0;  // 0 = Bilinear, 1 = UDR1, 2 = UDR2, 3 = UDR3

    private const float RotationSpeed = 0.12f;
    private const float OrbitRadius = 360f;
    private const int LightCount = 14;

    private const float BoxSize = 80f;
    private const float ColumnSpacing = 120f;
    private const int MaxBoxesPerColumn = 10;

    public override void SetupECS()
    {
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<Geometry>();
        HRCGISystem = ECS.AddSystem<HRCGI>();
        RCGISystem = ECS.AddSystem<RCGI>(enabled: false);
        BilinearSystem = ECS.AddSystem<Bilinear>();
        UDR1System = ECS.AddSystem<UDR1>(enabled: false);
        UDR2System = ECS.AddSystem<UDR2>(enabled: false);
        UDR3System = ECS.AddSystem<UDR3>(enabled: false);
        Gizmos = ECS.AddSystem<GizmosRenderer>();

        base.SetupECS();
    }

    public override void SetupScene()
    {
        CreateRotatingLights();
        CreateOccluders();
        CreateMouseLight();
        CreateCenterTriangles();
        UpdateUDRInput();

        base.SetupScene();
    }

    private void CreateCenterTriangles()
    {
        var center = Renderer.Window.GetScreenCenter();

        // Large outer triangle - black border
        float sizeOuter = 500f;
        CreateBorderedTriangle(center, sizeOuter, Color.Black);

        // Smaller inner triangle - purple emissive
        float sizeInner = 150f;
        CreateBorderedTriangle(center, sizeInner, new Color(180, 0, 255));
    }

    private void CreateBorderedTriangle(Vector2 center, float size, Color emissive)
    {
        int id = ECS.CreateEntity();

        ref var transform = ref ECS.AddComponent<Transform>(id);
        ref var triangle = ref ECS.AddComponent<Triangle2D>(id);
        ref var material = ref ECS.AddComponent<Material>(id);

        // Triangle centroid is at y=0.117 in UV space (below box center)
        // Offset the box up so the visual centroid aligns with screen center
        float centroidOffsetY = size * 0.117f;
        transform.Position = new Vector3(center.X - size / 2, center.Y - size / 2 - centroidOffsetY, 0);
        transform.Rotation = Vector3.UnitX;

        triangle.Size = new Vector2(size);
        triangle.Bordered = true;

        material.Albedo = new Color(0, 0, 0, 200);
        material.Emissive = emissive;
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

            RotatingLightIds[i] = CreateLight(new Vector2(x, y), 25f, color, color);

            // Add motion tracking for rotating lights (these move every frame)
            ECS.AddComponent<MotionTrackable>(RotatingLightIds[i]);
        }
    }

    private void CreateOccluders()
    {
        var screen = Renderer.Window.GetScreenSize();
        var center = Renderer.Window.GetScreenCenter();

        int columnsPerSide = (int)((screen.X / 2 - ColumnSpacing) / ColumnSpacing);
        float maxDistance = screen.X / 2f;  // Scale alpha across full screen half-width

        var occluderList = new List<int>();

        for (int col = 1; col <= columnsPerSide; col++)
        {
            float distanceFromCenter = col * ColumnSpacing;

            // Farther from center = more alpha, closer = less alpha
            float alpha = (distanceFromCenter / maxDistance) * 1.0f;
            alpha = Math.Clamp(alpha, 0f, 1.0f);
            byte alphaByte = (byte)(alpha * 255);

            // Each column farther gains +1 top and +1 bottom box
            int extraBoxes = col - 1;
            int boxesInColumn = MaxBoxesPerColumn + extraBoxes * 2;

            // Left column
            float leftX = center.X - distanceFromCenter;
            for (int row = 0; row < boxesInColumn; row++)
            {
                float y = center.Y - (boxesInColumn * BoxSize / 2) + row * BoxSize + BoxSize / 2;
                occluderList.Add(CreateOccluder(new Vector2(leftX, y), BoxSize, alphaByte));
            }

            // Right column
            float rightX = center.X + distanceFromCenter;
            for (int row = 0; row < boxesInColumn; row++)
            {
                float y = center.Y - (boxesInColumn * BoxSize / 2) + row * BoxSize + BoxSize / 2;
                occluderList.Add(CreateOccluder(new Vector2(rightX, y), BoxSize, alphaByte));
            }
        }

    }

    private void CreateMouseLight()
    {
        var mouse = Mouse.GetState();
        MouseLightId = CreateLight(new Vector2(mouse.X, mouse.Y), 100f, new Color(0, 0, 0, 128), new Color(0, 0, 0, 128));

        // Add motion tracking for mouse-controlled light
        ECS.AddComponent<MotionTrackable>(MouseLightId);

        PrevMouse = mouse;
    }

    private int CreateLight(Vector2 position, float radius, Color color, Color emissive)
    {
        int id = ECS.CreateEntity();

        ref var transform = ref ECS.AddComponent<Transform>(id);
        ref var circle = ref ECS.AddComponent<Circle2D>(id);
        ref var material = ref ECS.AddComponent<Material>(id);

        transform.Position = new Vector3(position, id);
        transform.Rotation = Vector3.UnitX;

        material.Albedo = color;
        material.Emissive = emissive;

        circle.Radius = radius;

        return id;
    }

    private int CreateOccluder(Vector2 position, float size)
    {
        int id = ECS.CreateEntity();

        ref var transform = ref ECS.AddComponent<Transform>(id);
        ref var rect = ref ECS.AddComponent<Rectangle2D>(id);
        ref var material = ref ECS.AddComponent<Material>(id);

        transform.Position = new Vector3(position, id);
        transform.Rotation = Vector3.UnitX;

        rect.Size = new Vector2(size);

        material.Albedo = new Color(30, 30, 0, 90);
        material.Emissive = Color.Black;

        return id;
    }

    private int CreateOccluder(Vector2 position, float size, byte alpha)
    {
        int id = ECS.CreateEntity();

        ref var transform = ref ECS.AddComponent<Transform>(id);
        ref var rect = ref ECS.AddComponent<Rectangle2D>(id);
        ref var material = ref ECS.AddComponent<Material>(id);

        transform.Position = new Vector3(position, id);
        transform.Rotation = Vector3.UnitX;

        rect.Size = new Vector2(size);

        material.Albedo = new Color((byte)0, (byte)0, (byte)0, alpha);
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
            transform.Position = new Vector3(x, y, RotatingLightIds[i]);  // Use entity ID for Z
        }

        // Update mouse light (always on top)
        ref var mouseTransform = ref ECS.GetComponent<Transform>(MouseLightId);
        mouseTransform.Position = new Vector3(mouse.X, mouse.Y, 999999f);

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

            // Right click + moving: spawn black dots continuously
            if (mouse.RightButton == ButtonState.Pressed)
            {
                if (!HasLastRightPaintPos)
                {
                    // First paint point
                    PaintBlackDotAt(mousePos);
                    LastRightPaintPos = mousePos;
                    HasLastRightPaintPos = true;
                }
                else
                {
                    // Interpolate between last position and current to fill gaps
                    float distance = Vector2.Distance(LastRightPaintPos, mousePos);
                    if (distance >= PaintSpacing)
                    {
                        Vector2 direction = Vector2.Normalize(mousePos - LastRightPaintPos);
                        float traveled = PaintSpacing;

                        while (traveled <= distance)
                        {
                            Vector2 paintPos = LastRightPaintPos + direction * traveled;
                            PaintBlackDotAt(paintPos);
                            traveled += PaintSpacing;
                        }

                        LastRightPaintPos = mousePos;
                    }
                }
            }
            else
            {
                HasLastRightPaintPos = false;
            }

            // Middle click + moving: spawn white half-opaque dots continuously
            if (mouse.MiddleButton == ButtonState.Pressed)
            {
                if (!HasLastMiddlePaintPos)
                {
                    // First paint point
                    PaintWhiteDotAt(mousePos);
                    LastMiddlePaintPos = mousePos;
                    HasLastMiddlePaintPos = true;
                }
                else
                {
                    // Interpolate between last position and current to fill gaps
                    float distance = Vector2.Distance(LastMiddlePaintPos, mousePos);
                    if (distance >= PaintSpacing)
                    {
                        Vector2 direction = Vector2.Normalize(mousePos - LastMiddlePaintPos);
                        float traveled = PaintSpacing;

                        while (traveled <= distance)
                        {
                            Vector2 paintPos = LastMiddlePaintPos + direction * traveled;
                            PaintWhiteDotAt(paintPos);
                            traveled += PaintSpacing;
                        }

                        LastMiddlePaintPos = mousePos;
                    }
                }
            }
            else
            {
                HasLastMiddlePaintPos = false;
            }
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
        Gizmos.Set("Scene", "Left: Rainbow | Right: Black | Middle: White (50%)");
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
        BilinearSystem.SetInputSource(inputSource);
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
                BilinearSystem.Dispose();
                BilinearSystem.Enabled = false;
                break;
            case 1:
                UDR1System.Dispose();
                UDR1System.Enabled = false;
                break;
            case 2:
                UDR2System.Dispose();
                UDR2System.Enabled = false;
                break;
            case 3:
                UDR3System.Dispose();
                UDR3System.Enabled = false;
                break;
        }

        // Cycle to next mode (0 = Bilinear, 1 = UDR1, 2 = UDR2, 3 = UDR3)
        UDRMode = (UDRMode + 1) % 4;

        // Enable new UDR system
        switch (UDRMode)
        {
            case 0:
                BilinearSystem.Initialize();
                BilinearSystem.Enabled = true;
                break;
            case 1:
                UDR1System.Initialize();
                UDR1System.Enabled = true;
                break;
            case 2:
                UDR2System.Initialize();
                UDR2System.Enabled = true;
                break;
            case 3:
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
            0 => "Bilinear",
            1 => "UDR1 (Spatial)",
            2 => "UDR2 (Spatial + Temporal)",
            3 => "UDR3 (Lanczos + Temporal)",
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
        CreateLight(position, PaintRadius, color, color);
    }

    private void PaintBlackDotAt(Vector2 position)
    {
        var color = Color.Black;

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

        // No overlap, create new black dot
        CreateLight(position, PaintRadius, color, Color.Black);
    }

    private void PaintWhiteDotAt(Vector2 position)
    {
        var color = new Color(255, 255, 255, 128); // White with 50% opacity

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
                material.Emissive = new Color(255, 255, 255, 128);
                return;
            }
        }

        // No overlap, create new white dot
        CreateLight(position, PaintRadius, color, new Color(255, 255, 255, 128));
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

            CreateLight(new Vector2(x, y), 3f, color, color);
        }
    }

    public override void Render()
    {
        base.Render();
    }
}
