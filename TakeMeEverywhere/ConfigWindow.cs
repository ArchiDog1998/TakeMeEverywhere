using Dalamud.Interface.Windowing;
using ECommons.Commands;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ImGuiNET;

namespace TakeMeEverywhere;

internal class ConfigWindow : Window
{
    public bool IsAutoRecording = true;

    public ConfigWindow() : base("Take me EveryWhere", ImGuiWindowFlags.None, false)
    {
    }

    public override void Draw()
    {
        CmdManager.DrawHelp();

        CmdManager.DisplayCommandHelp("/takeme", 
            $"pos {Svc.ClientState.TerritoryType},{Player.Object?.Position.X ?? 0:F2},{Player.Object?.Position.Y ?? 0:F2},{Player.Object?.Position.Z ?? 0:F2}",
            "set the position from 'TerritoryId, X, Y, Z' or 'TerritoryId, X, Z'.");

        ImGui.Separator();

        ImGui.Checkbox("Auto Recording", ref IsAutoRecording);

        if (IsAutoRecording) return;

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
