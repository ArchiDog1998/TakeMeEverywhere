using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Roy_T.AStar.Graphs;
using System.Numerics;

namespace TakeMeEverywhere;

public sealed class TakeMeEverywherePlugin : IDalamudPlugin, IDisposable
{
    public enum PathState
    {
        None,
        Run,
        Fly,
    }

    public TakeMeEverywherePlugin(DalamudPluginInterface pluginInterface)
    {
        ECommonsMain.Init(pluginInterface, this, Module.ObjectFunctions);

        Service.Init(pluginInterface);

        Svc.Framework.Update += Update;
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
        Service.Position?.TryMakeValid();
        if (Service.Position == null) return;

        //CheckTeleport(Service.Position);
        RunToPosition(Service.Position);
    }

    private static void CheckTeleport(DesiredPosition position)
    {
        if (Svc.ClientState.TerritoryType == position.TerritoryId) return;
        if (Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Casting]) return;

        var aetheryte = position.Aetheryte;

        //TODO: Better safe teleport!
        aetheryte?.Teleport();
    }

    private PathState _state = PathState.None;
    private void RunToPosition(DesiredPosition position)
    {
        if (!position.IsValid) return;

        var hasNaviPts = Service.Runner.NaviPts.Count > 0;

        if (Svc.ClientState.TerritoryType != position.TerritoryId)
        {
            _state = PathState.None;
            return;
        }
        else if (!hasNaviPts)
        {
            _state = PathState.None;

            //Arrived!
            if (Vector3.DistanceSquared(position.Position, Player.Object.Position)
            < Service.Runner.Precision * Service.Runner.Precision)
            {
                Service.Position = null;
                return;
            }
        }

        switch (_state)
        {
            case PathState.Fly when XIVRunner.XIVRunner.IsFlying && hasNaviPts:
            case PathState.Run when !XIVRunner.XIVRunner.IsFlying && hasNaviPts:
                return;

            case PathState.None:
            case PathState.Fly:
                FindGraphWithNodes(position, Service.RunNodes);
                _state = PathState.Run;
                return;

            case PathState.Run:
                FindGraphWithNodes(position, Service.FlyNodes);
                _state = PathState.Fly;
                return;
        }
    }

    private static void FindGraphWithNodes(DesiredPosition position, INode[] nodes)
    {
        if (nodes == null || nodes.Length == 0) return;

        var start = Player.Object.Position;
        var end = position.Position;

        var startNode = nodes.MinBy(a => Vector3.DistanceSquared(start, a.Position));
        var endNode = nodes.MinBy(a => Vector3.DistanceSquared(end, a.Position));

        var finder = new Roy_T.AStar.Paths.PathFinder();
        var path = finder.FindPath(startNode, endNode, float.MaxValue);

        //TODO: better intro and outro.
        foreach (var edge in path.Edges)
        {
            Service.Runner.NaviPts.Enqueue(edge.End.Position);
        }
        Service.Runner.NaviPts.Enqueue(end);

        Svc.Log.Info($"Run {Service.Runner.NaviPts.Count}");
    }
}
