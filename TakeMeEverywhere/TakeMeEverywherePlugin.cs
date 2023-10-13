using Dalamud.Plugin;
using ECommons;

namespace TakeMeEverywhere;

public sealed class TakeMeEverywherePlugin : IDalamudPlugin, IDisposable
{
    XIVPainter.XIVPainter _painter;
    XIVRunner.XIVRunner _runner;
    public TakeMeEverywherePlugin(DalamudPluginInterface pluginInterface)
    {
        ECommonsMain.Init(pluginInterface, this, Module.DalamudReflector, Module.ObjectFunctions);
        _painter = XIVPainter.XIVPainter.Create(pluginInterface, "Take Me Everywhere");
        _runner = XIVRunner.XIVRunner.Create(pluginInterface);
    }

    public void Dispose()
    {
        _painter.Dispose();
        _runner.Dispose();
        ECommonsMain.Dispose();
    }
}
