using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace com.radiant.engine.bundle;

public class HolographicRC : core.System
{
    const int FC = 4, CC = 4;
    
    SceneSDF sdf;
    GizmosRenderer giz;
    Effect fx;
    SpriteBatch sb;
    Texture2D px;
    
    RenderTarget2D[,] cascades;
    RenderTarget2D[] resolved;
    RenderTarget2D final;
    
    Vector2 world;
    Vector2[] sizes;
    
    KeyboardState pk;
    int dbg = 0;
    string[] dbgN;

    public override void Initialize()
    {
        base.Initialize();
        sdf = Scene.ECS.GetSystem<SceneSDF>();
        giz = Scene.ECS.GetSystem<GizmosRenderer>();
        
        fx = RenderPipeline.Window.Content.Load<Effect>("shaders/HRC");
        sb = new SpriteBatch(RenderPipeline.GraphicsDevice);
        px = new Texture2D(RenderPipeline.GraphicsDevice, 1, 1);
        px.SetData([Color.White]);
        
        var vp = RenderPipeline.GraphicsDevice.Viewport;
        world = new Vector2(vp.Width, vp.Height);
        
        CalcSizes();
        CreateRT();
        BuildDbg();
        
        giz.AddSection("HRC", "HRC", Color.Cyan);
        pk = Keyboard.GetState();
    }

    void CalcSizes()
    {
        sizes = new Vector2[CC];
        for (int c = 0; c < CC; c++)
        {
            float intrv = MathF.Pow(2, c);
            int numProbes = (int)MathF.Floor(world.X / intrv);
            sizes[c] = new Vector2(numProbes * intrv, world.Y);
        }
    }

    void CreateRT()
    {
        var d = RenderPipeline.GraphicsDevice;
        var fmt = SurfaceFormat.Color;
        
        cascades = new RenderTarget2D[FC, CC];
        resolved = new RenderTarget2D[FC];
        
        for (int f = 0; f < FC; f++)
        {
            for (int c = 0; c < CC; c++)
            {
                int w = (int)sizes[c].X, h = (int)sizes[c].Y;
                cascades[f, c] = new RenderTarget2D(d, w, h, false, fmt, DepthFormat.None);
            }
            resolved[f] = new RenderTarget2D(d, (int)world.X, (int)world.Y, false, fmt, DepthFormat.None);
        }
        final = new RenderTarget2D(d, (int)world.X, (int)world.Y, false, fmt, DepthFormat.None);
    }

    void BuildDbg()
    {
        var n = new System.Collections.Generic.List<string> { "Final" };
        for (int f = 0; f < FC; f++) n.Add($"F{f}");
        for (int f = 0; f < FC; f++) for (int c = 0; c < CC; c++) n.Add($"F{f}C{c}");
        n.Add("Emissive"); n.Add("Absorption");
        dbgN = n.ToArray();
    }

    void SetSamplers()
    {
        var d = RenderPipeline.GraphicsDevice;
        for (int i = 1; i <= 10; i++)
            d.SamplerStates[i] = SamplerState.LinearClamp;
    }

    public override void Update()
    {
        var k = Keyboard.GetState();
        if (k.IsKeyDown(Keys.F3) && !pk.IsKeyDown(Keys.F3)) dbg = (dbg + 1) % dbgN.Length;
        pk = k;
        
        var em = sdf.GetEmissiveTexture();
        var ab = sdf.GetAbsorptionTexture();
        
        for (int f = 0; f < FC; f++)
        {
            for (int c = 0; c < CC; c++)
            {
                RenderCascade(f, c, em, ab);
            }
            
            Copy(cascades[f, CC - 1], resolved[f]);
        }
        
        Compose();
        
        giz.ClearSection("HRC");
        giz.AddSectionString("HRC", $"{dbgN[dbg]} (F3)");
    }

    void RenderCascade(int f, int c, Texture2D em, Texture2D ab)
    {
        var d = RenderPipeline.GraphicsDevice;
        d.SetRenderTarget(cascades[f, c]);
        d.Clear(Color.Transparent);
        SetSamplers();
        
        fx.Parameters["EmissiveTex"]?.SetValue(em);
        fx.Parameters["AbsorpTex"]?.SetValue(ab);
        fx.Parameters["WorldSize"]?.SetValue(world);
        fx.Parameters["CascadeSize"]?.SetValue(sizes[c]);
        fx.Parameters["CascadeIndex"]?.SetValue((float)c);
        fx.Parameters["Frustum"]?.SetValue((float)f);
        
        if (c > 0)
        {
            fx.Parameters["PrevCasc"]?.SetValue(cascades[f, c - 1]);
            fx.Parameters["PrevSize"]?.SetValue(sizes[c - 1]);
        }
        else
        {
            fx.Parameters["PrevCasc"]?.SetValue(px);
            fx.Parameters["PrevSize"]?.SetValue(new Vector2(1, 1));
        }
        
        fx.CurrentTechnique = fx.Techniques["Merge"];
        sb.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, null, null, fx);
        sb.Draw(px, new Rectangle(0, 0, (int)sizes[c].X, (int)sizes[c].Y), Color.White);
        sb.End();
        
        d.SetRenderTarget(null);
    }

    void Copy(RenderTarget2D src, RenderTarget2D dst)
    {
        var d = RenderPipeline.GraphicsDevice;
        d.SetRenderTarget(dst);
        sb.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp);
        sb.Draw(src, new Rectangle(0, 0, dst.Width, dst.Height), Color.White);
        sb.End();
        d.SetRenderTarget(null);
    }

    void Compose()
    {
        var d = RenderPipeline.GraphicsDevice;
        d.SetRenderTarget(final);
        d.Clear(Color.Black);
        SetSamplers();
        
        fx.Parameters["Frust0"]?.SetValue(resolved[0]);
        fx.Parameters["Frust1"]?.SetValue(resolved[1]);
        fx.Parameters["Frust2"]?.SetValue(resolved[2]);
        fx.Parameters["Frust3"]?.SetValue(resolved[3]);
        fx.Parameters["CascadeSize"]?.SetValue(world);
        
        fx.CurrentTechnique = fx.Techniques["Compose"];
        sb.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, null, null, fx);
        sb.Draw(px, new Rectangle(0, 0, (int)world.X, (int)world.Y), Color.White);
        sb.End();
        d.SetRenderTarget(null);
    }

    public override void Render()
    {
        Texture2D tex = GetDbgTex();
        RenderPipeline.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp);
        RenderPipeline.SpriteBatch.Draw(tex, RenderPipeline.GraphicsDevice.Viewport.Bounds, Color.White);
        RenderPipeline.SpriteBatch.End();
    }

    Texture2D GetDbgTex()
    {
        if (dbg == 0) return final;
        int x = dbg - 1;
        if (x < FC) return resolved[x];
        x -= FC;
        if (x < FC * CC) return cascades[x / CC, x % CC];
        x -= FC * CC;
        if (x == 0) return sdf.GetEmissiveTexture();
        return sdf.GetAbsorptionTexture();
    }

    public RenderTarget2D GetOutput() => final;

    public override void Dispose()
    {
        fx?.Dispose(); sb?.Dispose(); px?.Dispose(); final?.Dispose();
        for (int f = 0; f < FC; f++)
        {
            resolved[f]?.Dispose();
            for (int c = 0; c < CC; c++)
                cascades[f, c]?.Dispose();
        }
    }
}