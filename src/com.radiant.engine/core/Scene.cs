using System;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.core;

public class Scene : IGameObject
{
    public int Id { get; private set; }

    public ECS ECS { get; private set; }

    public GameTime GameTime { get; set; }

    public float DeltaTime { get; set; }

    public Renderer Renderer { get; set; }

    public void Initialize()
    {
        ECS = new ECS(this, Renderer);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public virtual void SetupECS()
    {
        ECS.Initialize();
    }

    public virtual void SetupScene()
    {
    }

    public virtual void Update() { }
    public virtual void FixedUpdate() { }
    public virtual void Render() { }
    public virtual void LateRender() { }

    internal void InternalUpdate()
    {
        Update();
        ECS.Update();
    }

    internal void InternalFixedUpdate()
    {
        FixedUpdate();
        ECS.FixedUpdate();
    }

    internal void InternalRender()
    {
        if (!Renderer.Window.IsActive)
            return;

        Render();
        ECS.Render();
    }

    internal void InternalLateRender()
    {
        if (!Renderer.Window.IsActive)
            return;

        LateRender();
        ECS.LateRender();
    }
}