using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Commands;
using ECommons.DalamudServices;

namespace TakeMeEverywhere;

public sealed class TakeMeEverywherePlugin : IDalamudPlugin, IDisposable
{
    private readonly ConfigWindow _window;

    private readonly WindowSystem _windowSystem;

    private static TakeMeEverywherePlugin? plugin;
    public static bool IsOpen => plugin?._window.IsOpen ?? false;
    public static bool IsAutoRecording => plugin?._window.IsAutoRecording ?? false;

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
        if (!Service.Runner.MovingValid)
        {
            Service.Position = null;
            return;
        }

        if(Service.Position == null)
        {
            Service.AutoRecordPath();
        }
        else
        {
            Service.Position.GoTo();
        }
    }

    [Cmd("/takeme", "command for take me to somewhere")]
    [SubCmd("flag", "Take me to the map flag")]
    [SubCmd("cancel", "Cancel to take me to the map flag")]
    internal void OnCommand(string _, string arguments)
    {
        if (arguments.StartsWith("flag"))
        {
            Service.Position ??= DesiredPosition.FromFlag();
            return;
        }
        else if (arguments.StartsWith("cancel"))
        {
            Service.Position = null;
            Service.Runner.NaviPts.Clear();
            return;
        }
        else if (arguments.StartsWith("pos"))
        {
            var values = arguments.Split(',');
            if (values == null || values.Length < 3)
            {
                Svc.Chat.PrintError("Wrong format!, please do it like  'TerritoryId, X, Y, Z' or 'TerritoryId, X, Z'.");
                return;
            }
            
            if (!uint.TryParse(values[0].Trim(), out var territory))
            {
                Svc.Chat.PrintError("Territory id is not a unit, please write a unit!");
                return;
            }

            const string locationFormat = "The location format isn't correct, please write float!";
            float[]? floats = null;
            try
            {
                floats = values.Skip(1).Select(f => float.Parse(f.Trim())).ToArray();
            }
            catch
            {
                Svc.Chat.PrintError(locationFormat);
                return;
            }

            if (floats == null || floats.Length < 2)
            {
                Svc.Chat.PrintError(locationFormat);
                return;
            }

            Service.Position = new DesiredPosition(territory, floats.Length == 2 ? new System.Numerics.Vector3(floats[0], float.NaN, floats[1]) : new System.Numerics.Vector3(floats[0], floats[1], floats[2]));
            return;
        }

        OnOpenConfigUi();
    }
}
