using System;

namespace com.radiant.engine.core;

public interface IGameObject : IDisposable
{
    void Initialize();
    void Update();
    void FixedUpdate();
    void Render();
    void LateRender();
}