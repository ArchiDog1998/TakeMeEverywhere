using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;

namespace TakeMeEverywhere;

public sealed class TakeMeEverywherePlugin : IDalamudPlugin, IDisposable
{
    public TakeMeEverywherePlugin(DalamudPluginInterface pluginInterface)
    {
        ECommonsMain.Init(pluginInterface, this, Module.ObjectFunctions);

        Service.Init(pluginInterface);

        Svc.Framework.Update += Update;

        Callback.InstallHook();
    }

    public void Dispose()
    {
        Svc.Framework.Update -= Update;

        Service.Dispose();
        ECommonsMain.Dispose();
    }

    private void Update(IFramework framework)
    {
#if DEBUG
        Service.Position ??= DesiredPosition.FromFlag();
#endif
        Service.Position?.GoTo();
    }
}
