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

[Flags]
public enum PauseGroup : byte
{
    None = 0,
    Gameplay = 1,
    Animation = 2
}

/// <summary>
/// Marks a system as pausable. Specify which pause groups affect this system.
/// Systems with Gameplay are skipped when ECS.GameplayPaused is true.
/// Systems with Animation are skipped when ECS.AnimationPaused is true.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class PausableAttribute : Attribute
{
    public PauseGroup Groups { get; }
    public PausableAttribute() => Groups = PauseGroup.Gameplay;
    public PausableAttribute(PauseGroup groups) => Groups = groups;
}

public enum RenderLayer : byte { World = 0, Gameplay = 1, Overlay = 2, UI = 3 }

public abstract class System
{
    public Scene Scene;

    public GameTime GameTime;

    public Renderer Renderer;

    public bool Enabled = true;
    internal PauseGroup PauseGroups;

    public virtual RenderLayer RenderLayer => RenderLayer.Gameplay;

    public virtual void Initialize() {}

    public virtual void Dispose() {}

    public virtual void Update() {}

    public virtual void FixedUpdate() {}

    public virtual void Render() {}

    public virtual void LateRender() {}

    public virtual void OnResize() {}
}
