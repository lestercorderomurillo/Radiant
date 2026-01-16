using System;
using System.Runtime.InteropServices;
using com.radiant.engine.runtime;

class Program
{
    static void Main()
    {
        var gameClient = new GameClient();
        
        gameClient.Run();
    }
}