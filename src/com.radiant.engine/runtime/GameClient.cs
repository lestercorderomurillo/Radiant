using com.radiant.engine.core;

namespace com.radiant.engine.runtime;

public class GameClient
{
    private Window Window;

    private GameLoop GameLoop;

    public void Run()
    {
        GameLoop = new GameLoop();

        GameLoop.AddScene(new PacmanMazeLevelScene());

        Window = new Window(GameLoop);
        Window.Run();
    }
}