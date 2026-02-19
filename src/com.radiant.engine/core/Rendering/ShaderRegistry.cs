using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace com.radiant.engine.core;

/// <summary>
/// Caches compiled Effect shaders loaded from the content pipeline.
/// Shaders are loaded from Content/shaders/ and cached by name.
/// </summary>
internal class ShaderRegistry : IDisposable
{
    private readonly Dictionary<string, Effect> Cache = new();
    private readonly ContentManager Content;

    public ShaderRegistry(ContentManager content)
    {
        Content = content;
    }

    /// <summary>
    /// Loads a shader by name, caching it for subsequent calls.
    /// </summary>
    /// <param name="name">Shader path relative to Content/shaders/.</param>
    public Effect Load(string name)
    {
        if (!Cache.TryGetValue(name, out var shader))
        {
            shader = Content.Load<Effect>($"shaders/{name}");
            Cache[name] = shader;
        }
        return shader;
    }

    /// <summary>
    /// Gets a cached shader by name, loading it if not yet cached.
    /// </summary>
    public Effect Get(string name) => Load(name);

    /// <summary>
    /// Disposes and removes a shader from the cache.
    /// </summary>
    /// <returns>True if the shader was found and released.</returns>
    public bool Release(string name)
    {
        if (Cache.TryGetValue(name, out var shader))
        {
            shader.Dispose();
            Cache.Remove(name);
            return true;
        }
        return false;
    }

    /// <summary>Disposes all cached shaders.</summary>
    public void Dispose()
    {
        foreach (var shader in Cache.Values)
            shader?.Dispose();
        Cache.Clear();
    }
}
