using com.radiant.engine.core;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public class PaintBrush : core.System
{
    public float PaintRadius { get; set; } = 8f;
    public float PaintSpacing { get; set; } = 3f;
    public float HueSpeed { get; set; } = 0.008f;
    public int ExcludeEntityId { get; set; } = -1;

    private float RainbowHue;
    private Vector2 LastPaintPos;
    private bool HasLastPaintPos;
    private Vector2 LastRightPaintPos;
    private bool HasLastRightPaintPos;

    public override void Update()
    {
        if (!Renderer.IsActive) return;

        var mouse = Mouse.GetState();
        var mousePos = Renderer.ScreenToWorld(new Vector2(mouse.X, mouse.Y));

        bool leftDown = mouse.LeftButton == ButtonState.Pressed;
        bool rightDown = mouse.RightButton == ButtonState.Pressed;

        if (leftDown && !rightDown)
            HandlePaintStroke(ref LastPaintPos, ref HasLastPaintPos, mousePos, true);
        else
            HasLastPaintPos = false;

        if (rightDown && !leftDown)
            HandlePaintStroke(ref LastRightPaintPos, ref HasLastRightPaintPos, mousePos, false);
        else
            HasLastRightPaintPos = false;
    }

    private void HandlePaintStroke(ref Vector2 lastPos, ref bool hasLast, Vector2 currentPos, bool rainbow)
    {
        if (!hasLast)
        {
            PaintAt(currentPos, rainbow);
            lastPos = currentPos;
            hasLast = true;
            return;
        }

        float distance = Vector2.Distance(lastPos, currentPos);
        if (distance < PaintSpacing) return;

        Vector2 direction = Vector2.Normalize(currentPos - lastPos);
        float traveled = PaintSpacing;

        while (traveled <= distance)
        {
            PaintAt(lastPos + direction * traveled, rainbow);
            traveled += PaintSpacing;
        }

        lastPos = currentPos;
    }

    private void PaintAt(Vector2 position, bool rainbow)
    {
        var color = rainbow ? LightFactory.HueToRGB(RainbowHue) : Color.Black;
        if (rainbow) RainbowHue = (RainbowHue + HueSpeed) % 1f;

        var nearby = Scene.ECS.InRadius(new Vector3(position, 0), PaintRadius);
        foreach (int entityId in nearby)
        {
            if (Scene.ECS.HasComponent<Circle2D>(entityId) && entityId != ExcludeEntityId)
            {
                ref var material = ref Scene.ECS.GetComponent<Material>(entityId);
                material.Albedo = color;
                material.Emissive = color;
                return;
            }
        }

        LightFactory.CreateLight(Scene.ECS, position, PaintRadius, color, color);
    }
}
