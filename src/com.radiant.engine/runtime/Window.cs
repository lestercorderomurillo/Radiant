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

    private WinForms.Form Form;
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
        GraphicsDeviceManager.PreferredBackBufferWidth = (int)(1920 * 1.5);
        GraphicsDeviceManager.PreferredBackBufferHeight = (int)(1080 * 1.5);
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
        Form = (WinForms.Form)WinForms.Form.FromHandle(Window.Handle);
        Form.Resize += OnFormResize;

        GameLoop.Initialize(this);
    }

    private void OnFormResize(object sender, EventArgs e)
    {
        if (Form.ClientSize.Width > 0 && Form.ClientSize.Height > 0)
        {
            // Update backbuffer to match window size
            GraphicsDeviceManager.PreferredBackBufferWidth = Form.ClientSize.Width;
            GraphicsDeviceManager.PreferredBackBufferHeight = Form.ClientSize.Height;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            GameLoop.Dispose();
        }
        base.Dispose(disposing);
    }
}