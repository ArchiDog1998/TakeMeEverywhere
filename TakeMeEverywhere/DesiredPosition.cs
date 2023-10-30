using Dalamud.Memory;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.GeneratedSheets;
using Roy_T.AStar.Graphs;
using System.Numerics;

namespace TakeMeEverywhere;

public class DesiredPosition
{
    public enum PathState
    {
        None,
        Run,
        Fly,
    }

    public readonly uint TerritoryId;

    private Vector3 _position;
    public Vector3 Position
    {
        get => _position;
        init
        {
            if (value == _position) return;

            _position = value;

            var id = TerritoryId;
            var pt = new Vector2(value.X, value.Z);

            var closest = AetheryteInfo.AetheryteInfos
                .Where(a => a.Aetheryte.Territory.Row == id)
                .OrderBy(a => a.Location == default ? float.MaxValue : (a.Location - pt).LengthSquared())
                .FirstOrDefault();

            if (Svc.ClientState.TerritoryType == id && Player.Available
                && (pt - closest.Location).LengthSquared()
                > (pt - new Vector2(Player.Object.Position.X, Player.Object.Position.Z)).LengthSquared())
            {
                Aetheryte = null;
            }
            else
            {
                Aetheryte = closest;
            }
        }
    }

    public bool IsValid => !float.IsNaN(Position.Y);

    public AetheryteInfo? Aetheryte;

    public DesiredPosition(uint territory, Vector3 position)
    {
        TerritoryId = territory;
        Position = position;
    }

    public void TryMakeValid()
    {
        if (IsValid)
        {
            return;
        }
        if (Svc.ClientState?.TerritoryType != TerritoryId) return;

        _position.Y = Raycast(Position);

        unsafe static float Raycast(in Vector3 point)
        {
            int* unknown = stackalloc int[] { 0x4000, 0, 0x4000, 0 };

            RaycastHit hit = default;

            return FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->BGCollisionModule
                ->RaycastEx(&hit, new Vector3(point.X, 100, point.Z), -Vector3.UnitY, float.MaxValue, 1, unknown)
                ? hit.Point.Y : float.NaN;
        }
    }

    public unsafe static DesiredPosition? FromFlag()
    {
        var instance = AgentMap.Instance();

        if (instance == null || instance->IsFlagMarkerSet == 0) return null;

        var marker = instance->FlagMapMarker;

        var map = Svc.Data.GetExcelSheet<Map>()?.GetRow(marker.MapId);

        var pos = new Vector3(marker.XFloat + map?.OffsetX ?? 0, float.NaN,
            marker.YFloat + map?.OffsetY ?? 0);

        return new DesiredPosition(marker.TerritoryId, pos);
    }

    private bool _isGoingTo = false;
    public void GoTo()
    {
        if (!Player.Available) return;

        if (_isGoingTo) return;
        _isGoingTo = true;

        Task.Run(() =>
        {
            try
            {
                if (!IsValid)
                {
                    TryMakeValid();
                }

                if (Aetheryte.HasValue)
                {
                    Teleport(Aetheryte.Value);
                }
                else if (IsValid)
                {
                    RunToPosition();
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Warning(ex, "Failed to Go to the specific position");
            }
            finally
            {
                _isGoingTo = false;
            }
        });
    }

    private PathState _state = PathState.None;
    private void RunToPosition()
    {
        var hasNaviPts = Service.Runner.NaviPts.Count > 0;

        if (Svc.ClientState.TerritoryType != TerritoryId)
        {
            _state = PathState.None;
            return;
        }
        else if (!hasNaviPts)
        {
            _state = PathState.None;

            //Arrived!
            if (Vector3.DistanceSquared(Position, Player.Object.Position)
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
                if (FindGraphWithNodes(false))
                {
                    _state = PathState.Run;
                }
                else
                {
                    _state = PathState.None;
                    Service.Position = null;
                    Svc.Log.Info("Failed to calculate the path.");
                }
                return;

            case PathState.Run:
                if (FindGraphWithNodes(true))
                {
                    _state = PathState.Fly;
                }
                else
                {
                    _state = PathState.None;
                    Service.Position = null;
                    Svc.Log.Info("Failed to calculate the path.");
                }
                return;
        }

        bool FindGraphWithNodes(bool isFly)
        {
            var nodes = isFly ? Service.FlyNodes : Service.RunNodes;

            if (nodes == null || nodes.Length == 0) return false;

            var start = Player.Object.Position;
            var end = Position;

            if(isFly && CanSee(start, end))
            {
                Service.Runner.NaviPts.Enqueue(end);
                return true;
            }

            var startNode = nodes.OrderBy(a => Vector3.DistanceSquared(start, a.Position))
                .FirstOrDefault();
            var endNode = nodes.OrderBy(a => Vector3.DistanceSquared(end, a.Position))
                .FirstOrDefault();

            if (startNode == null) return false;
            if (endNode == null) return false;

            var finder = new Roy_T.AStar.Paths.PathFinder();
            var path = finder.FindPath(startNode, endNode, float.MaxValue);

            foreach (var edge in path.Edges)
            {
                Service.Runner.NaviPts.Enqueue(edge.End.Position);
            }
            Service.Runner.NaviPts.Enqueue(end);

            return true;

            unsafe static bool CanSee(in Vector3 from, in Vector3 to)
            {
                int* unknown = stackalloc int[] { 0x2000, 0, 0x2000, 0 };

                var ptA = from + Vector3.UnitY;
                var ptB = to + Vector3.UnitY;
                var dir = ptB - ptA;
                RaycastHit hit = default;

                return FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->BGCollisionModule
                    ->RaycastEx(&hit, ptA, dir, dir.Length(), 1, unknown);
            }
        }
    }

    private void Teleport(AetheryteInfo aetheryte)
    {
        if (Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Casting]) return;
        if (Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas]) return;
        if (Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas51]) return;

        //Teleported to the target.
        if (aetheryte.IsAround)
        {
            Aetheryte = null;
            return;
        }

        //for the case is aetheryte.
        if (aetheryte.IsAttuned)
        {
            aetheryte.Teleport();
            return;
        }

        var parent = aetheryte.ParentAetheryte;

        if (parent.IsAround)
        {
            FireToAetherNet(aetheryte);
        }
        else
        {
            parent.Teleport();
        }
    }

    DateTime _nextClickingTime = DateTime.Now;
    private unsafe bool FireToAetherNet(AetheryteInfo aetheryte)
    {
        var playerPosition = Player.Object.Position;
        var aetheryteObj = Svc.Objects
            .Where(o => o.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Aetheryte
                && o.IsTargetable)
            .MinBy(o => (o.Position - playerPosition).LengthSquared());

        if (aetheryteObj == null) return false;

        if ((aetheryteObj.Position - playerPosition).LengthSquared() > 6 * 6)
        {
            //Run to the aetheryte.
            if (Service.Runner.NaviPts.Any())
            {
                if ((Service.Runner.NaviPts.Peek() - aetheryteObj.Position).LengthSquared() > 1)
                {
                    Service.Runner.NaviPts.Clear();
                }
            }
            else
            {
                Service.Runner.NaviPts.Enqueue(aetheryteObj.Position);
            }

        }
        else if(_nextClickingTime < DateTime.Now) 
        {
            if (Service.Runner.NaviPts.Any())
            {
                Service.Runner.NaviPts.Clear();
            }

            //Fire ui
            var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("TelepotTown");
            var menu = (AddonSelectString*)Svc.GameGui.GetAddonByName("SelectString");
            if (addon != null)
            {
                for (int i = 1; i <= 52; i++)
                {
                    var item = addon->UldManager.NodeList[16]->GetAsAtkComponentNode()->Component->UldManager.NodeList[i];
                    var text = MemoryHelper.ReadSeString(&item->GetAsAtkComponentNode()->Component->UldManager.NodeList[3]->GetAsAtkTextNode()->NodeText).TextValue.Trim();
                    if (text != aetheryte.Aetheryte.AethernetName.Value?.Name.RawString) continue;

                    var aetherytes = AetheryteInfo.AetheryteInfos
                        .Where(a => a.Aetheryte.AethernetGroup == aetheryte.Aetheryte.AethernetGroup)
                        .ToArray();

                    for (int index = 0; index < aetherytes.Length; index++)
                    {
                        if (aetherytes[index].Aetheryte.RowId != aetheryte.Aetheryte.RowId) continue;

                        Svc.Log.Debug($"Called {index}");
                        Callback.Fire(addon, true, 11, index);
                        Callback.Fire(addon, true, 11, index);
                        _nextClickingTime = DateTime.Now.AddSeconds(0.5);
                        return true;
                    };
                }

                Aetheryte = null;
                return true;
            }
            else if(menu != null)
            {
                Callback.Fire((AtkUnitBase*)menu, true, 0);
                _nextClickingTime = DateTime.Now.AddSeconds(0.5);
            }
            //Open ui.
            else
            {
                Svc.Targets.Target = aetheryteObj;
                TargetSystem.Instance()->InteractWithObject(aetheryteObj.Struct(), false);
                _nextClickingTime = DateTime.Now.AddSeconds(0.5);
            }
        }
        return false;
    }
}
