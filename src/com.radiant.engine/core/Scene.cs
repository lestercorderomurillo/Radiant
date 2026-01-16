using System;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;

namespace com.radiant.engine.core;

public class Scene : GameObject
{
    public int Id { get; private set; }
    
    public ECS ECS { get; private set; }

    public GameTime GameTime { get; set; }

    public float DeltaTime { get; set; }

    public RenderPipeline RenderPipeline { get; set; }

    public override void Initialize()
    {
        ECS = new ECS(this, RenderPipeline);
    }

    public override void Dispose()
    {
        
    }

    public virtual void SetupECS()
    {
        ECS.Initialize();
    }

    public virtual void SetupScene()
    {
    }

    public override void Update()
    {
        ECS.Update();
    }

    public override void FixedUpdate()
    {
        ECS.FixedUpdate();
    }
    
    public override void Render()
    {
        if (!RenderPipeline.Window.IsActive)
            return;

        ECS.Render();
    }

    public override void LateRender()
    {
        if (!RenderPipeline.Window.IsActive)
            return;

        ECS.LateRender();
    }
}