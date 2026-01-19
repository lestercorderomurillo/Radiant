using System;
using com.radiant.engine.runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

public abstract class System
{
    public Scene Scene;

    public GameTime GameTime;

    public Renderer Renderer;

    public bool Enabled = true;

    public virtual void Initialize() {}

    public virtual void Dispose() {}

    public virtual void Update() {}

    public virtual void FixedUpdate() {}

    public virtual void Render() {}

    public virtual void LateRender() {}

    public virtual void OnResize() {}
}