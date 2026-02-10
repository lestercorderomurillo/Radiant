using System;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class RunAfterAttribute : Attribute
{
    public Type[] SystemTypes { get; }
    public RunAfterAttribute(params Type[] systemTypes) => SystemTypes = systemTypes;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class RunBeforeAttribute : Attribute
{
    public Type[] SystemTypes { get; }
    public RunBeforeAttribute(params Type[] systemTypes) => SystemTypes = systemTypes;
}

/// <summary>
/// Marks a system as pausable. Only systems with this attribute are skipped when ECS.Paused is true.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class PausableAttribute : Attribute { }

public enum RenderLayer : byte { World = 0, Gameplay = 1, Overlay = 2, UI = 3 }

public abstract class System
{
    public Scene Scene;

    public GameTime GameTime;

    public Renderer Renderer;

    public bool Enabled = true;
    internal bool IsPausable;

    public virtual RenderLayer RenderLayer => RenderLayer.Gameplay;

    public virtual void Initialize() {}

    public virtual void Dispose() {}

    public virtual void Update() {}

    public virtual void FixedUpdate() {}

    public virtual void Render() {}

    public virtual void LateRender() {}

    public virtual void OnResize() {}
}
