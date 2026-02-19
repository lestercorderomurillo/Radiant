using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

#pragma warning disable CS0618
namespace com.radiant.engine.core;

public partial class Renderer
{
    private readonly RenderTargetBinding[] TwoTargetBindings = new RenderTargetBinding[2];
    private readonly RenderTargetBinding[] ThreeTargetBindings = new RenderTargetBinding[3];
    private readonly RenderTargetBinding[] FourTargetBindings = new RenderTargetBinding[4];
    private readonly Stack<RenderTargetBinding[]> RenderTargetStack = new();
    private RenderTargetBinding[] CurrentTargets = null;

    private const int BindingPoolSize = 16;
    private readonly RenderTargetBinding[][] BindingPool2 = new RenderTargetBinding[BindingPoolSize][];
    private readonly RenderTargetBinding[][] BindingPool3 = new RenderTargetBinding[BindingPoolSize][];
    private readonly RenderTargetBinding[][] BindingPool4 = new RenderTargetBinding[BindingPoolSize][];
    private int BindingPool2Index = 0;
    private int BindingPool3Index = 0;
    private int BindingPool4Index = 0;

    private void InitializeBindingPools()
    {
        for (int i = 0; i < BindingPoolSize; i++)
        {
            BindingPool2[i] = new RenderTargetBinding[2];
            BindingPool3[i] = new RenderTargetBinding[3];
            BindingPool4[i] = new RenderTargetBinding[4];
        }
    }

    /// <summary>
    /// Pushes current render targets onto an internal stack. Use with PopTargets to
    /// restore state after nested rendering operations without GPU synchronization.
    /// </summary>
    public Renderer PushTargets()
    {
        RenderTargetStack.Push(CurrentTargets);
        return this;
    }

    /// <summary>
    /// Pops and restores render targets from the internal stack.
    /// </summary>
    public Renderer PopTargets()
    {
        if (RenderTargetStack.Count > 0)
        {
            var targets = RenderTargetStack.Pop();
            CommitTextures();
            if (targets == null)
                Device.SetRenderTarget(SceneRT);
            else
                Device.SetRenderTargets(targets);
            CurrentTargets = targets;
        }
        return this;
    }

    /// <summary>Sets a single render target (or null for SceneRT).</summary>
    public Renderer SetTarget(RenderTarget2D target)
    {
        CommitTextures();
        Device.SetRenderTarget(target ?? SceneRT);
        CurrentTargets = target != null ? [new RenderTargetBinding(target)] : null;
        return this;
    }

    /// <summary>Sets two render targets for MRT rendering.</summary>
    public Renderer SetTargets(RenderTarget2D target0, RenderTarget2D target1)
    {
        CommitTextures();
        TwoTargetBindings[0] = new RenderTargetBinding(target0);
        TwoTargetBindings[1] = new RenderTargetBinding(target1);
        Device.SetRenderTargets(TwoTargetBindings);
        var pooled = BindingPool2[BindingPool2Index];
        BindingPool2Index = (BindingPool2Index + 1) & (BindingPoolSize - 1);
        pooled[0] = TwoTargetBindings[0];
        pooled[1] = TwoTargetBindings[1];
        CurrentTargets = pooled;
        return this;
    }

    /// <summary>Sets three render targets for MRT rendering.</summary>
    public Renderer SetTargets(RenderTarget2D target0, RenderTarget2D target1, RenderTarget2D target2)
    {
        CommitTextures();
        ThreeTargetBindings[0] = new RenderTargetBinding(target0);
        ThreeTargetBindings[1] = new RenderTargetBinding(target1);
        ThreeTargetBindings[2] = new RenderTargetBinding(target2);
        Device.SetRenderTargets(ThreeTargetBindings);
        var pooled = BindingPool3[BindingPool3Index];
        BindingPool3Index = (BindingPool3Index + 1) & (BindingPoolSize - 1);
        pooled[0] = ThreeTargetBindings[0];
        pooled[1] = ThreeTargetBindings[1];
        pooled[2] = ThreeTargetBindings[2];
        CurrentTargets = pooled;
        return this;
    }

    /// <summary>Sets four render targets for MRT rendering.</summary>
    public Renderer SetTargets(RenderTarget2D target0, RenderTarget2D target1, RenderTarget2D target2, RenderTarget2D target3)
    {
        CommitTextures();
        FourTargetBindings[0] = new RenderTargetBinding(target0);
        FourTargetBindings[1] = new RenderTargetBinding(target1);
        FourTargetBindings[2] = new RenderTargetBinding(target2);
        FourTargetBindings[3] = new RenderTargetBinding(target3);
        Device.SetRenderTargets(FourTargetBindings);
        var pooled = BindingPool4[BindingPool4Index];
        BindingPool4Index = (BindingPool4Index + 1) & (BindingPoolSize - 1);
        pooled[0] = FourTargetBindings[0];
        pooled[1] = FourTargetBindings[1];
        pooled[2] = FourTargetBindings[2];
        pooled[3] = FourTargetBindings[3];
        CurrentTargets = pooled;
        return this;
    }

    /// <summary>Sets multiple render targets for MRT rendering.</summary>
    public Renderer SetTargets(params RenderTarget2D[] targets)
    {
        CommitTextures();
        var bindings = new RenderTargetBinding[targets.Length];
        for (int i = 0; i < targets.Length; i++)
            bindings[i] = new RenderTargetBinding(targets[i]);
        Device.SetRenderTargets(bindings);
        CurrentTargets = bindings;
        return this;
    }

    /// <summary>Sets render targets from pre-built bindings array.</summary>
    public Renderer SetTargets(params RenderTargetBinding[] bindings)
    {
        CommitTextures();
        Device.SetRenderTargets(bindings);
        CurrentTargets = bindings;
        return this;
    }

    /// <summary>
    /// Clears the current render target(s) to the specified color.
    /// </summary>
    /// <param name="color">Clear color (defaults to Black).</param>
    public Renderer Clear(Color? color = null)
    {
        Device.Clear(color ?? Color.Black);
        return this;
    }
}
