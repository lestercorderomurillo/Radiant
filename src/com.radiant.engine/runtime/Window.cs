// Window.cs - Ensure proper presentation
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using WinForms = System.Windows.Forms;

namespace com.radiant.engine.runtime;

public class Window : Game
{
    public GraphicsDeviceManager GraphicsDeviceManager;

    public GameLoop GameLoop;

    public SpriteBatch SpriteBatch;

    // Resize handling
    private WinForms.Form _form;
    public bool ResizePending { get; private set; }

    public Window(GameLoop gameLoop)
    {
        GraphicsDeviceManager = new GraphicsDeviceManager(this);

        Window.AllowUserResizing = true;
        Window.IsBorderless = false;
        Window.Title = "Radiant Engine";

        IsMouseVisible = true;
        IsFixedTimeStep = false;

        GraphicsDeviceManager.SynchronizeWithVerticalRetrace = false;
        GraphicsDeviceManager.PreferredDepthStencilFormat = DepthFormat.None;
        GraphicsDeviceManager.PreferredBackBufferWidth = (int)(3840 * 0.5f);
        GraphicsDeviceManager.PreferredBackBufferHeight = (int)(2160 * 0.5f);
        GraphicsDeviceManager.GraphicsProfile = GraphicsProfile.HiDef;
        GraphicsDeviceManager.IsFullScreen = false;
        GraphicsDeviceManager.HardwareModeSwitch = true;
        GraphicsDeviceManager.PreferMultiSampling = false;

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

        // Hook into Form resize events for WindowsDX
        _form = (WinForms.Form)WinForms.Form.FromHandle(Window.Handle);
        _form.Resize += OnFormResize;

        GameLoop.Initialize(this);
    }

    private void OnFormResize(object sender, EventArgs e)
    {
        if (_form.ClientSize.Width > 0 && _form.ClientSize.Height > 0)
        {
            // Update backbuffer to match window size
            GraphicsDeviceManager.PreferredBackBufferWidth = _form.ClientSize.Width;
            GraphicsDeviceManager.PreferredBackBufferHeight = _form.ClientSize.Height;
            GraphicsDeviceManager.ApplyChanges();

            // Signal resize to systems
            ResizePending = true;

            // Force game loop to run while Windows message loop is blocked during resize
            Tick();
        }
    }

    public void ClearResizePending()
    {
        ResizePending = false;
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