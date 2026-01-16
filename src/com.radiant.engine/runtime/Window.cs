// Window.cs - Ensure proper presentation
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace com.radiant.engine.runtime;

public class Window : Game
{
    public GraphicsDeviceManager GraphicsDeviceManager;

    public GameLoop GameLoop;

    public SpriteBatch SpriteBatch;

    public Window(GameLoop gameLoop)
    {
        GraphicsDeviceManager = new GraphicsDeviceManager(this);
                
        Window.AllowUserResizing = false;
        Window.IsBorderless = false;
        Window.Title = "Radiant Engine";
        
        IsMouseVisible = true;
        IsFixedTimeStep = false;

        GraphicsDeviceManager.SynchronizeWithVerticalRetrace = false;
        GraphicsDeviceManager.PreferredDepthStencilFormat = DepthFormat.None;
        GraphicsDeviceManager.PreferredBackBufferWidth = 3840;
        GraphicsDeviceManager.PreferredBackBufferHeight = 2160;
        GraphicsDeviceManager.GraphicsProfile = GraphicsProfile.HiDef;
        GraphicsDeviceManager.IsFullScreen = false;
        GraphicsDeviceManager.HardwareModeSwitch = true;
        GraphicsDeviceManager.PreferMultiSampling = true;
         
        GraphicsDeviceManager.ApplyChanges();

        GameLoop = gameLoop;
        Content.RootDirectory = "Content";
    }

    protected override void Initialize()
    {
        base.Initialize();

        SpriteBatch = new SpriteBatch(GraphicsDevice);
        GraphicsDevice.PresentationParameters.PresentationInterval = PresentInterval.Immediate;
        GameLoop.SpriteBatch = SpriteBatch;
        
        GameLoop.Initialize(this);
    }

    public Vector2 GetScreenCenter()
    {
        return new Vector2(GraphicsDeviceManager.PreferredBackBufferWidth / 2, GraphicsDeviceManager.PreferredBackBufferHeight / 2);
    }

    public Vector2 GetScreenSize()
    {
        return new Vector2(GraphicsDeviceManager.PreferredBackBufferWidth, GraphicsDeviceManager.PreferredBackBufferHeight);
    }

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        GameLoop.GameTime = gameTime;
        GameLoop.Update();

        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();
    }

    protected override void Draw(GameTime gameTime)
    {
        GameLoop.GameTime = gameTime;
        GameLoop.Render();
    }
}