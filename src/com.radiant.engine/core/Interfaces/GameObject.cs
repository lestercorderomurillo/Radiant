using System;

namespace com.radiant.engine.core;

public class GameObject : IDisposable
{
    public virtual void Initialize() { }

    public virtual void Dispose() { }

    public virtual void Update(){ }

    public virtual void FixedUpdate(){ }

    public virtual void Render() { }
    
    public virtual void LateRender() { }
}