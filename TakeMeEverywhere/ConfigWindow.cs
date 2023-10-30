using Dalamud.Interface.Windowing;
using ECommons.Commands;
using ECommons.DalamudServices;
using ImGuiNET;

namespace TakeMeEverywhere;

internal class ConfigWindow : Window
{
    public ConfigWindow() : base("Take me EveryWhere", ImGuiWindowFlags.None, false)
    {
    }

    public override void Draw()
    {
        CmdManager.DrawHelp();

        ImGui.Separator();

        if (ImGui.Button("Select or Add Node"))
        {
            DoOneThing(() =>
            {
                Service.SelectOrAddNode();
                Service.SaveTerritoryGraph();
            });
        }

        if (ImGui.Button("Connect Node"))
        {
            DoOneThing(() =>
            {
                Service.ConnectNode();
                Service.SaveTerritoryGraph();
            });
        }

        ImGui.Separator();

        if (ImGui.Button("Delete Node"))
        {
            DoOneThing(() =>
            {
                Service.DeleteNode();
                Service.SaveTerritoryGraph();
            });
        }

        if (ImGui.Button("Disconnect Node"))
        {
            DoOneThing(() =>
            {
                Service.DisconnectNode();
                Service.SaveTerritoryGraph();
            });
        }
    }

    private static bool _isRunning = false;
    private static void DoOneThing(Action action)
    {
        if (action == null) return;
        if (_isRunning) return;
        _isRunning = true;
        Task.Run(() =>
        {
            try
            {
                action();
            }
            catch(Exception e)
            {
                Svc.Log.Warning(e, "Failed to modify the graph.");
            }
            finally
            {
                _isRunning = false;
            }
        });
    }
}
