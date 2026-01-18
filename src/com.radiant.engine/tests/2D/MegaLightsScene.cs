using com.radiant.engine.bundle;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;

namespace com.radiant.engine.core;

public class MegaLightsScene : Scene
{
    private int mouseEmitterEntityId;
    private MouseState previousMouseState;
    private int[] boxEntityIds; // Track box entities
    private int[] occluderEntityIds; // Track scattered occluder entities
    private float rotationSpeed = 0.40f; // Rotation speed in radians per second
    private float currentRotation = 0f; // Current rotation angle
    private Vector2 screenCenter; // Center point for rotation
    private float boxRadius = 300; // Radius of the ring
    private Random random = new Random(); // Random number generator for occluder placement
    private bool isMoving = true; // Toggle for box movement
    private KeyboardState previousKeyboardState; // Track keyboard state for toggle

    // Personalizable sizes
    private float boxSize = 75; // Size for rotating light boxes
    private float occluderSize = 75; // Size for occluders
    private float centerExclusionRadius = 400; // Radius around center to keep clear of occluders

    public override void SetupECS()
    {
        // Add systems
        ECS.AddSystem<PerformanceMonitor>();
        ECS.AddSystem<GizmosRenderer>();
        ECS.AddSystem<SceneGeometry>();
        ECS.AddSystem<RCGI>();

        base.SetupECS();
    }

    public override void SetupScene()
    {
        screenCenter = Renderer.Window.GetScreenCenter();

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

        boxEntityIds = new int[boxCount]; // Initialize array to track box entities

        for (int i = 0; i < boxCount; i++)
        {
            int boxEntity = ECS.CreateEntity(); // Now returns int
            boxEntityIds[i] = boxEntity; // Store entity ID

            ref var transform = ref ECS.AddComponent<Transform>(boxEntity);
            ref var rect = ref ECS.AddComponent<Rectangle2D>(boxEntity);
            ref var material = ref ECS.AddComponent<Material>(boxEntity);

            float angle = (float)i / boxCount * MathHelper.TwoPi;
            float x = screenCenter.X + boxRadius * (float)Math.Cos(angle);
            float y = screenCenter.Y + boxRadius * (float)Math.Sin(angle);

            byte r = (byte)(Math.Sin(angle) * 127 + 128);
            byte g = (byte)(Math.Sin(angle + MathHelper.TwoPi / 3) * 127 + 128);
            byte b = (byte)(Math.Sin(angle + MathHelper.TwoPi * 2 / 3) * 127 + 128);

            transform.Position = new Vector3(x, y, 0);
            transform.Rotation = new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0);

            rect.Size = new Vector2(boxSize, boxSize);

            material.Albedo = new Color(r, g, b);
            material.Emissive = new Color(r, g, b);
        }
    }

    private void CreateScatteredOccluders()
    {
        int targetOccluderCount = 100;
        int maxAttempts = 100; // Allow more attempts to find valid positions

        occluderEntityIds = new int[targetOccluderCount];

        // Get screen bounds for scattering
        Vector2 screenSize = Renderer.Window.GetScreenSize();
        float margin = 200; // Keep occluders away from screen edges

        int occluderIndex = 0;
        int attempts = 0;

        while (occluderIndex < targetOccluderCount && attempts < maxAttempts)
        {
            attempts++;

            // Generate random position within screen bounds (with margin)
            float x = margin + (float)random.NextDouble() * (screenSize.X - 2 * margin);
            float y = margin + (float)random.NextDouble() * (screenSize.Y - 2 * margin);
            var position = new Vector3(x, y, 0);

            // Check if position is far enough from center
            float distanceFromCenter = Vector2.Distance(new Vector2(position.X, position.Y), screenCenter);
            if (distanceFromCenter < centerExclusionRadius)
            {
                continue; // Skip this position, too close to center
            }

            // Position is valid, create occluder
            int occluderId = ECS.CreateEntity();
            occluderEntityIds[occluderIndex] = occluderId;

            ref var transform = ref ECS.AddComponent<Transform>(occluderId);
            ref var rect = ref ECS.AddComponent<Rectangle2D>(occluderId);
            ref var material = ref ECS.AddComponent<Material>(occluderId);

            transform.Position = position;
            transform.Rotation = new Vector3(1, 0, 0);

            // Same size for all occluders
            rect.Size = new Vector2(occluderSize, occluderSize);

            // Dark gray occluders with no emission
            material.Albedo = new Color(40, 40, 40); // Dark gray for visibility
            material.Emissive = Color.Black; // No light emission

            occluderIndex++;
        }

        // Resize array to actual number of created occluders (in case we couldn't place all 100)
        if (occluderIndex < targetOccluderCount)
        {
            Array.Resize(ref occluderEntityIds, occluderIndex);
        }
    }

    private void CreateMouseEmitter()
    {
        mouseEmitterEntityId = ECS.CreateEntity(); // Now returns int

        ref var emitterTransform = ref ECS.AddComponent<Transform>(mouseEmitterEntityId);
        ref var emitterRect = ref ECS.AddComponent<Rectangle2D>(mouseEmitterEntityId);
        ref var emitterMaterial = ref ECS.AddComponent<Material>(mouseEmitterEntityId);

        // Initialize emitter
        MouseState mouse = Mouse.GetState();
        Vector2 mousePos = new Vector2(mouse.X, mouse.Y);

        // Default rotation (facing right)
        emitterTransform.Position = new Vector3(mousePos.X, mousePos.Y, 0);
        emitterTransform.Rotation = new Vector3(1, 0, 0);
        emitterRect.Size = new Vector2(100, 100);
        emitterMaterial.Albedo = new Color(255, 255, 100);
        emitterMaterial.Emissive = new Color(255, 255, 100); // Bright yellow

        // Initialize mouse state
        previousMouseState = mouse;
    }

    public override void Update()
    {
        // Check for Space key to toggle movement
        KeyboardState currentKeyboardState = Keyboard.GetState();
        if (currentKeyboardState.IsKeyDown(Keys.Space) && previousKeyboardState.IsKeyUp(Keys.Space))
        {
            isMoving = !isMoving;
        }
        previousKeyboardState = currentKeyboardState;

        // Update rotation angle only if moving
        if (isMoving)
        {
            currentRotation += rotationSpeed * DeltaTime;
        }

        // Update box positions
        for (int i = 0; i < boxEntityIds.Length; i++)
        {
            ref var transform = ref ECS.GetComponent<Transform>(boxEntityIds[i]);

            float originalAngle = (float)i / boxEntityIds.Length * MathHelper.TwoPi;
            float newAngle = originalAngle + currentRotation;

            // Calculate new position
            float x = screenCenter.X + boxRadius * (float)Math.Cos(newAngle);
            float y = screenCenter.Y + boxRadius * (float)Math.Sin(newAngle);

            transform.Position = new Vector3(x, y, 0);
            transform.Rotation = new Vector3((float)Math.Cos(newAngle), (float)Math.Sin(newAngle), 0);
        }

        // Get current mouse state
        MouseState currentMouseState = Mouse.GetState();
        Vector2 mousePosition = new Vector2(currentMouseState.X, currentMouseState.Y);

        // Update mouse emitter position
        ref var emitterTransform = ref ECS.GetComponent<Transform>(mouseEmitterEntityId);

        emitterTransform.Position = new Vector3(mousePosition.X, mousePosition.Y, 0);

        // Create new emitter on left click
        if (currentMouseState.LeftButton == ButtonState.Pressed &&
            previousMouseState.LeftButton == ButtonState.Released)
        {
            SpawnEmitter(mousePosition);
        }

        // Create new occluder on right click
        if (currentMouseState.RightButton == ButtonState.Pressed &&
            previousMouseState.RightButton == ButtonState.Released)
        {
            SpawnOccluder(mousePosition);
        }

        // Store mouse state for next frame
        previousMouseState = currentMouseState;
    }

    private void SpawnEmitter(Vector2 position)
    {
        // Create a new emitter entity
        int lightId = ECS.CreateEntity();

        ref var transform = ref ECS.AddComponent<Transform>(lightId);
        ref var rect = ref ECS.AddComponent<Rectangle2D>(lightId);
        ref var material = ref ECS.AddComponent<Material>(lightId);

        transform.Position = new Vector3(position.X, position.Y, 0);
        transform.Rotation = new Vector3(1, 0, 0);
        rect.Size = new Vector2(100, 100);
        material.Albedo = new Color(255, 255, 255);
        material.Emissive = new Color(255, 255, 255); // Bright white
    }

    private void SpawnOccluder(Vector2 position)
    {
        // Create a new occluder entity
        int occluderId = ECS.CreateEntity();

        ref var transform = ref ECS.AddComponent<Transform>(occluderId);
        ref var rect = ref ECS.AddComponent<Rectangle2D>(occluderId);
        ref var material = ref ECS.AddComponent<Material>(occluderId);

        transform.Position = new Vector3(position.X, position.Y, 0);
        transform.Rotation = new Vector3(1, 0, 0);
        rect.Size = new Vector2(40, 40); // Larger size for occluders

        // Non-emissive material (black emissive = no light emission)
        material.Albedo = Color.Red; // Gray color for visibility
        material.Emissive = Color.Black; // No light emission
    }
}