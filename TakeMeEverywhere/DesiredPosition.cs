using Dalamud.Memory;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.GeneratedSheets;
using System.Numerics;

namespace TakeMeEverywhere;

public class DesiredPosition 
{
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

            Aetheryte = AetheryteInfo.AetheryteInfos
                .Where(a => a.Aetheryte.Territory.Row == id)
                .OrderBy(a => (a.Location - pt).LengthSquared())
                .FirstOrDefault();
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

        return new DesiredPosition(marker.TerritoryId,
            new Vector3(marker.XFloat + map?.OffsetX ?? 0, float.NaN,
            marker.YFloat + map?.OffsetY ?? 0));
    }

    public void Goto()
    {
        Teleport();
        Run();
    }

    private void Run()
    {

    }

    private void Teleport()
    {
        if (!Aetheryte.HasValue) return;
        var aetheryte = Aetheryte.Value;

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
            FireToAetherNet();
        }
        else
        {
            parent.Teleport();
        }

        unsafe void FireToAetherNet()
        {
            if (!Player.Available) return;

            var playerPosition = Player.Object.Position;
            var aetheryteObj = Svc.Objects
                .Where(o => o.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Aetheryte
                    && o.IsTargetable)
                .MinBy(o => (o.Position - playerPosition).LengthSquared());

            if (aetheryteObj == null) return;

            if ((aetheryteObj.Position - playerPosition).LengthSquared() > 6 * 6)
            {
                //Run to the aetheryte.
                if((Service.Runner.NaviPts.Peek() - aetheryteObj.Position).LengthSquared() > 1)
                {
                    Service.Runner.NaviPts.Clear();
                    Service.Runner.NaviPts.Enqueue(aetheryteObj.Position);
                }
            }
            else
            {
                if (Service.Runner.NaviPts.Any())
                {
                    Service.Runner.NaviPts.Clear();
                }

                //File ui
                var addon = (AtkUnitBase*) Svc.GameGui.GetAddonByName("TelepotTown");
                var menu = (AtkUnitBase*) Svc.GameGui.GetAddonByName("SelectString");
                if (addon != null)
                {
                    List<string> arr = new();
                    for (int i = 1; i <= 52; i++)
                    {
                        var item = addon->UldManager.NodeList[16]->GetAsAtkComponentNode()->Component->UldManager.NodeList[i];
                        var text = MemoryHelper.ReadSeString(&item->GetAsAtkComponentNode()->Component->UldManager.NodeList[3]->GetAsAtkTextNode()->NodeText).TextValue.Trim();
                        if (text == "") break;
                        arr.Add(text);
                    }
                }
                //Open ui.
                else
                {
                    Svc.Targets.Target = aetheryteObj;
                    TargetSystem.Instance()->InteractWithObject(aetheryteObj.Struct(), false);
                }
            }
        }
    }
}
