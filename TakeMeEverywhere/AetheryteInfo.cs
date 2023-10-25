using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.GeneratedSheets;
using System.Numerics;

namespace TakeMeEverywhere;

public unsafe struct AetheryteInfo
{
    static AetheryteInfo[]? _aetheryteInfos;
    public static AetheryteInfo[] AetheryteInfos => _aetheryteInfos 
        ??= Svc.Data.GetExcelSheet<Aetheryte>()?
            .Select(a => new AetheryteInfo(a)).ToArray()
            ?? Array.Empty<AetheryteInfo>();

    public readonly Aetheryte Aetheryte;
    public readonly Vector2 Location;

    public readonly bool IsAttuned
    {
        get
        {
            if (Aetheryte == null) return false;

            var teleport = Telepo.Instance();
            if (teleport == null) return false;

            teleport->UpdateAetheryteList();
            foreach (var info in teleport->TeleportList.Span)
            {
                if (info.AetheryteId == Aetheryte.RowId)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public readonly bool IsAround
    {
        get
        {
            if (Svc.ClientState.TerritoryType != Aetheryte.Territory.Value?.RowId) return false;

            var loc = new Vector2(Player.Object.Position.X, Player.Object.Position.Z);

            return (loc - Location).LengthSquared() < 64;
        }
    }

    private int _parentIndex = -1;
    public AetheryteInfo ParentAetheryte
    {
        get
        {
            if (_parentIndex < 0)
            {
                var group = Aetheryte.AethernetGroup;
                _parentIndex = Array.IndexOf(AetheryteInfos,
                    AetheryteInfos.FirstOrDefault(a => a.Aetheryte.AethernetGroup == group && a.IsAttuned));

                if (_parentIndex < 0)
                {
                    return this;
                }
            }

            return AetheryteInfos[_parentIndex];
        }
    }

    public AetheryteInfo(Aetheryte aetheryte)
    {
        Aetheryte = aetheryte;

        var mapMarker = Svc.Data.GetExcelSheet<MapMarker>()?.FirstOrDefault(m => (m.DataType == (aetheryte.IsAetheryte ? 3 : 4) && m.DataKey == (aetheryte.IsAetheryte ? aetheryte.RowId : aetheryte.AethernetName.Value?.RowId)));

        if (mapMarker == null) return;

        var map = aetheryte.Territory.Value?.Map.Value;
        var size = map?.SizeFactor ?? 100f;
        Location = MapToWorld(new Vector2(ConvertMapMarkerToMapCoordinate(mapMarker.X, size) + map?.OffsetX ?? 0,
    ConvertMapMarkerToMapCoordinate(mapMarker.Y, size) + map?.OffsetY ?? 0), map!);

        static float ConvertMapMarkerToMapCoordinate(int pos, float scale)
        {
            float num = scale / 100f;
            var rawPosition = (int)((float)(pos - 1024.0) / num * 1000f);
            return ConvertRawPositionToMapCoordinate(rawPosition, scale);

            static float ConvertRawPositionToMapCoordinate(int pos, float scale)
            {
                float num = scale / 100f;
                return (float)((pos / 1000f * num + 1024.0) / 2048.0 * 41.0 / num + 1.0);
            }
        }
    }

    /// <summary>
    /// Takes the given map coordinate (Ex. 13.5) and converts it to a world coordinate.
    /// </summary>
    /// <param name="value">Map Coordinate</param>
    /// <param name="scale">Map Scale</param>
    /// <param name="offset">Map X or Y offset</param>
    /// <returns></returns>
    public static float MapToWorld(float value, uint scale, int offset)
        => -offset * (scale / 100.0f) + 50.0f * (value - 1) * (scale / 100.0f);

    /// <summary>
    /// Convert the given X, Y map coordinates (Ex. 12.4 10.2) and converts it to a world coordinate.
    /// </summary>
    /// <param name="coordinates">Map Coordinates</param>
    /// <param name="map">Map</param>
    /// <returns>World Coordinate</returns>
    public static Vector2 MapToWorld(Vector2 coordinates, Lumina.Excel.GeneratedSheets.Map map)
    {
        var scalar = map.SizeFactor / 100.0f;

        var xWorldCoord = MapToWorld(coordinates.X, map.SizeFactor, map.OffsetX);
        var yWorldCoord = MapToWorld(coordinates.Y, map.SizeFactor, map.OffsetY);

        var objectPosition = new Vector2(xWorldCoord, yWorldCoord);
        var center = new Vector2(1024.0f, 1024.0f);

        return objectPosition / scalar - center / scalar;
    }

    private static DateTime _nextTeleTime = DateTime.Now;
    public readonly void Teleport()
    {
        if (ActionManager.Instance()->GetActionStatus(ActionType.Action, 5) != 0)
            return;

        if (Aetheryte == null)
        {
            Svc.Chat.Print("Invalid teleport target.");
            return;
        }

        if (DateTime.Now < _nextTeleTime) return;
        _nextTeleTime = DateTime.Now.AddSeconds(6);

        if (!IsAttuned) Svc.Chat.Print($"Teleport to the unsafe port {Aetheryte.PlaceName.Value?.Name ?? string.Empty} - {Aetheryte.AethernetName.Value?.Name ?? string.Empty}");
        Telepo.Instance()->Teleport(Aetheryte.RowId, (byte)Aetheryte.SubRowId);
    }
}
