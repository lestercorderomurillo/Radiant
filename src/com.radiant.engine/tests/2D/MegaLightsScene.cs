using com.radiant.engine.bundle;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;

namespace com.radiant.engine.core;

public class MegaLightsScene : Scene
{
    private int MouseEmitterEntityId;
    private MouseState PreviousMouseState;
    private int[] BoxEntityIds; // Track box entities
    private int[] OccluderEntityIds; // Track scattered occluder entities
    private float RotationSpeed = 0.10f; // Rotation speed in radians per second
    private float CurrentRotation = 0f; // Current rotation angle
    private Vector2 ScreenCenter; // Center point for rotation
    private float BoxRadius = 300; // Radius of the ring
    private Random Random = new Random(); // Random number generator for occluder placement
    private bool IsMoving = true; // Toggle for box movement
    private KeyboardState PreviousKeyboardState; // Track keyboard state for toggle
    private bool UseHolographic = true; // Toggle for rendering mode
    private HRCGI HolographicSystem;
    private RCGI RCGISystem;
    private GizmosRenderer Gizmos;

    // Personalizable sizes
    private float BoxSize = 75; // Size for rotating light boxes
    private float OccluderSize = 75; // Size for occluders
    private float CenterExclusionRadius = 400; // Radius around center to keep clear of occluders

    public override void SetupECS()
    {
        // Add systems
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<SceneGeometry>();
        HolographicSystem = ECS.AddSystem<HRCGI>();
        RCGISystem = ECS.AddSystem<RCGI>(enabled: false);
        Gizmos = ECS.AddSystem<GizmosRenderer>();

        base.SetupECS();
    }

    public override void SetupScene()
    {
        ScreenCenter = Renderer.Window.GetScreenCenter();

        // Create ring of boxes
        CreateRotatingBoxes();

        // Create scattered occluders
        CreateScatteredOccluders();

        // Create mouse-following emitter
        CreateMouseEmitter();

        base.SetupScene();
    }

    private void CreateRotatingBoxes()
    {
        int boxCount = 12;

        BoxEntityIds = new int[boxCount]; // Initialize array to track box entities

        for (int i = 0; i < boxCount; i++)
        {
            int boxEntity = ECS.CreateEntity();
            BoxEntityIds[i] = boxEntity;

            ref var transform = ref ECS.AddComponent<Transform>(boxEntity);
            ref var circle = ref ECS.AddComponent<Circle2D>(boxEntity);
            ref var material = ref ECS.AddComponent<Material>(boxEntity);

            float angle = (float)i / boxCount * MathHelper.TwoPi;
            float x = ScreenCenter.X + BoxRadius * (float)Math.Cos(angle);
            float y = ScreenCenter.Y + BoxRadius * (float)Math.Sin(angle);

            // Different colors per circle
            byte r = (byte)(Math.Sin(angle) * 127 + 128);
            byte g = (byte)(Math.Sin(angle + MathHelper.TwoPi / 3) * 127 + 128);
            byte b = (byte)(Math.Sin(angle + MathHelper.TwoPi * 2 / 3) * 127 + 128);

            transform.Position = new Vector3(x, y, 0);
            transform.Rotation = new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0);
            circle.Radius = BoxSize / 2;

            material.Albedo = new Color(r, g, b);
            material.Emissive = new Color(r, g, b);
        }
    }

    private void CreateScatteredOccluders()
    {
        int targetOccluderCount = 100;
        int maxAttempts = 100; // Allow more attempts to find valid positions

        OccluderEntityIds = new int[targetOccluderCount];

        // Get screen bounds for scattering
        Vector2 screenSize = Renderer.Window.GetScreenSize();
        float margin = 200; // Keep occluders away from screen edges

        int occluderIndex = 0;
        int attempts = 0;

        while (occluderIndex < targetOccluderCount && attempts < maxAttempts)
        {
            attempts++;

            // Generate random position within screen bounds (with margin)
            float x = margin + (float)Random.NextDouble() * (screenSize.X - 2 * margin);
            float y = margin + (float)Random.NextDouble() * (screenSize.Y - 2 * margin);
            var position = new Vector3(x, y, 0);

            // Check if position is far enough from center
            float distanceFromCenter = Vector2.Distance(new Vector2(position.X, position.Y), ScreenCenter);
            if (distanceFromCenter < CenterExclusionRadius)
            {
                continue; // Skip this position, too close to center
            }

            // Position is valid, create occluder
            int occluderId = ECS.CreateEntity();
            OccluderEntityIds[occluderIndex] = occluderId;

            ref var transform = ref ECS.AddComponent<Transform>(occluderId);
            ref var rect = ref ECS.AddComponent<Rectangle2D>(occluderId);
            ref var material = ref ECS.AddComponent<Material>(occluderId);

            transform.Position = position;
            transform.Rotation = new Vector3(1, 0, 0);

            // Same size for all occluders
            rect.Size = new Vector2(OccluderSize, OccluderSize);

            // Dark gray occluders with no emission
            material.Albedo = new Color(40, 40, 40); // Dark gray for visibility
            material.Emissive = Color.Black; // No light emission

            occluderIndex++;
        }

        // Resize array to actual number of created occluders (in case we couldn't place all 100)
        if (occluderIndex < targetOccluderCount)
        {
            Array.Resize(ref OccluderEntityIds, occluderIndex);
        }
    }

    private void CreateMouseEmitter()
    {
        MouseEmitterEntityId = ECS.CreateEntity();

        ref var emitterTransform = ref ECS.AddComponent<Transform>(MouseEmitterEntityId);
        ref var emitterCircle = ref ECS.AddComponent<Circle2D>(MouseEmitterEntityId);
        ref var emitterMaterial = ref ECS.AddComponent<Material>(MouseEmitterEntityId);

        // Initialize emitter
        MouseState mouse = Mouse.GetState();
        Vector2 mousePos = new Vector2(mouse.X, mouse.Y);

        emitterTransform.Position = new Vector3(mousePos.X, mousePos.Y, 0);
        emitterTransform.Rotation = new Vector3(1, 0, 0);
        emitterCircle.Radius = 50;
        emitterMaterial.Albedo = new Color(255, 255, 100);
        emitterMaterial.Emissive = new Color(255, 255, 100); // Bright yellow

        // Initialize mouse state
        PreviousMouseState = mouse;
    }

    public override void Update()
    {
        // Check for Space key to toggle movement
        KeyboardState currentKeyboardState = Keyboard.GetState();
        if (currentKeyboardState.IsKeyDown(Keys.Space) && PreviousKeyboardState.IsKeyUp(Keys.Space))
        {
            IsMoving = !IsMoving;
        }

        // Check for Tab key to toggle between Holographic and RCGI
        if (currentKeyboardState.IsKeyDown(Keys.Tab) && PreviousKeyboardState.IsKeyUp(Keys.Tab))
        {
            UseHolographic = !UseHolographic;
            if (UseHolographic)
            {
                RCGISystem.Dispose();
                RCGISystem.Enabled = false;
                HolographicSystem.Initialize();
                HolographicSystem.Enabled = true;
            }
            else
            {
                HolographicSystem.Dispose();
                HolographicSystem.Enabled = false;
                RCGISystem.Initialize();
                RCGISystem.Enabled = true;
            }
        }

        PreviousKeyboardState = currentKeyboardState;

        // Update rotation angle only if moving
        if (IsMoving)
        {
            CurrentRotation += RotationSpeed * DeltaTime;
        }

        // Update box positions
        for (int i = 0; i < BoxEntityIds.Length; i++)
        {
            ref var transform = ref ECS.GetComponent<Transform>(BoxEntityIds[i]);

            float originalAngle = (float)i / BoxEntityIds.Length * MathHelper.TwoPi;
            float newAngle = originalAngle + CurrentRotation;

            // Calculate new position
            float x = ScreenCenter.X + BoxRadius * (float)Math.Cos(newAngle);
            float y = ScreenCenter.Y + BoxRadius * (float)Math.Sin(newAngle);

            transform.Position = new Vector3(x, y, 0);
            transform.Rotation = new Vector3((float)Math.Cos(newAngle), (float)Math.Sin(newAngle), 0);
        }

        // Get current mouse state
        MouseState currentMouseState = Mouse.GetState();
        Vector2 mousePosition = new Vector2(currentMouseState.X, currentMouseState.Y);

        // Update mouse emitter position
        ref var emitterTransform = ref ECS.GetComponent<Transform>(MouseEmitterEntityId);

        emitterTransform.Position = new Vector3(mousePosition.X, mousePosition.Y, 0);

        // Create new emitter on left click
        if (currentMouseState.LeftButton == ButtonState.Pressed &&
            PreviousMouseState.LeftButton == ButtonState.Released)
        {
            SpawnEmitter(mousePosition);
        }

        // Create new occluder on right click
        if (currentMouseState.RightButton == ButtonState.Pressed &&
            PreviousMouseState.RightButton == ButtonState.Released)
        {
            SpawnOccluder(mousePosition);
        }

        // Store mouse state for next frame
        PreviousMouseState = currentMouseState;

        // Display current rendering mode
        string mode = UseHolographic ? "HRCGI" : "RCGI";
        Gizmos.Set("Renderer", $"Mode: {mode} (Tab to toggle)");
    }

    private void SpawnEmitter(Vector2 position)
    {
        // Create a new circle emitter entity
        int lightId = ECS.CreateEntity();

        ref var transform = ref ECS.AddComponent<Transform>(lightId);
        ref var circle = ref ECS.AddComponent<Circle2D>(lightId);
        ref var material = ref ECS.AddComponent<Material>(lightId);

        transform.Position = new Vector3(position.X, position.Y, 0);
        transform.Rotation = new Vector3(1, 0, 0);
        circle.Radius = 50;

        // Random color for the light
        byte r = (byte)Random.Next(128, 256);
        byte g = (byte)Random.Next(128, 256);
        byte b = (byte)Random.Next(128, 256);
        var color = new Color(r, g, b);

        material.Albedo = color;
        material.Emissive = color;
    }

    private void SpawnOccluder(Vector2 position)
    {
        // Create a new circle occluder entity
        int occluderId = ECS.CreateEntity();

        ref var transform = ref ECS.AddComponent<Transform>(occluderId);
        ref var circle = ref ECS.AddComponent<Circle2D>(occluderId);
        ref var material = ref ECS.AddComponent<Material>(occluderId);

        transform.Position = new Vector3(position.X, position.Y, 0);
        transform.Rotation = new Vector3(1, 0, 0);
        circle.Radius = 20;

        // Non-emissive material (black emissive = no light emission)
        material.Albedo = Color.Red;
        material.Emissive = Color.Red;
    }
}