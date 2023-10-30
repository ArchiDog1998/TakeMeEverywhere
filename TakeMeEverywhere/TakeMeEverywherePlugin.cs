using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Automation;
using ECommons.Commands;
using ECommons.DalamudServices;

namespace TakeMeEverywhere;

public sealed class TakeMeEverywherePlugin : IDalamudPlugin, IDisposable
{
    private readonly ConfigWindow _window;

    private readonly WindowSystem _windowSystem;

    private static TakeMeEverywherePlugin? plugin;
    public static bool IsOpen => plugin?._window.IsOpen ?? false;

    public TakeMeEverywherePlugin(DalamudPluginInterface pluginInterface)
    {
        plugin = this;

        ECommonsMain.Init(pluginInterface, this, Module.ObjectFunctions);

        Service.Init(pluginInterface);

        _window = new();

        _windowSystem = new();
        _windowSystem.AddWindow(_window);

        Svc.Framework.Update += Update;

        Svc.PluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;
        Svc.PluginInterface.UiBuilder.Draw += _windowSystem.Draw;

        Callback.InstallHook();
    }

    public void Dispose()
    {
        Svc.Framework.Update -= Update;

        Svc.PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
        Svc.PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;

        Service.Dispose();
        ECommonsMain.Dispose();
    }

    private void OnOpenConfigUi()
    {
        _window.IsOpen = true;
    }

    private void Update(IFramework framework)
    {
        Service.Position?.GoTo();
    }

    [Cmd("/takeme", "command for take me to somewhere")]
    [SubCmd("flag", "Take me to the map flag")]
    internal void OnCommand(string _, string arguments)
    {
        if (arguments.StartsWith("flag"))
        {
            Service.Position ??= DesiredPosition.FromFlag();
            return;
        }

        OnOpenConfigUi();
    }
}
