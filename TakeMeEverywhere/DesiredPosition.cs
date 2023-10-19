using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using System.Numerics;

namespace TakeMeEverywhere;

public struct DesiredPosition
{
    public uint TerritoryId;
    public Vector3 Position;

    public unsafe static DesiredPosition? FromFlag()
    {
        var marker = AgentMap.Instance()->FlagMapMarker;

        if (marker.TerritoryId == 0) return null;

        return new DesiredPosition()
        {
            TerritoryId = marker.TerritoryId,
            Position = new Vector3(marker.XFloat, float.NaN, marker.YFloat),
        };
    }

    public void CheckYValue()
    {
        if (!float.IsNaN(Position.Y)) return;

        if (Svc.ClientState.TerritoryType != TerritoryId) return;

        Position.Y = Raycast(Position);

        unsafe float Raycast(in Vector3 point)
        {
            int* unknown = stackalloc int[] { 0x4000, 0, 0x4000, 0 };

            RaycastHit hit = default;

            return FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->BGCollisionModule
                ->RaycastEx(&hit, point + Vector3.UnitY * 100, -Vector3.UnitY, float.MaxValue, 1, unknown) 
                ? hit.Point.Y : float.NaN;
        }
    }
}
