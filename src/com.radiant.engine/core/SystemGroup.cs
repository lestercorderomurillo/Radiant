using System;

namespace com.radiant.engine.core;

public class SystemGroup
{
    private readonly (string Name, System System)[] Entries;
    private int ActiveIndex;

    public string ActiveName => Entries[ActiveIndex].Name;
    public System Active => Entries[ActiveIndex].System;

    public SystemGroup(params (string name, System system)[] entries)
    {
        Entries = entries;
        ActiveIndex = Array.FindIndex(entries, e => e.system.Enabled);
        if (ActiveIndex < 0) ActiveIndex = 0;
    }

    public void Toggle()
    {
        var old = Entries[ActiveIndex].System;
        old.Dispose();
        old.Enabled = false;

        ActiveIndex = (ActiveIndex + 1) % Entries.Length;

        var next = Entries[ActiveIndex].System;
        next.Initialize();
        next.Enabled = true;
    }

    public void ForEach(Action<System> action)
    {
        for (int i = 0; i < Entries.Length; i++)
            action(Entries[i].System);
    }
}
