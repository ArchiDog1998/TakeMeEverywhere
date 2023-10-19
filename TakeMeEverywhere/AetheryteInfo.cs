using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.GeneratedSheets;
using System;
using System.Numerics;

namespace TakeMeEverywhere;

internal readonly unsafe struct AetheryteInfo
{
    public readonly Aetheryte Aetheryte;
    public readonly Vector2 Location;
    public bool IsAttuned
    {
        get
        {
            if (Aetheryte == null) return false;

            var teleport = Telepo.Instance();
            if (teleport == null) return false;

            if (!Player.Available) return true;

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

    public AetheryteInfo(Aetheryte aetheryte)
    {
        Aetheryte = aetheryte;

        var mapMarker = Svc.Data.GetExcelSheet<MapMarker>()?.FirstOrDefault(m => (m.DataType == (aetheryte.IsAetheryte ? 3 : 4) && m.DataKey == (aetheryte.IsAetheryte ? aetheryte.RowId : aetheryte.AethernetName.Value?.RowId)));

        if (mapMarker != null)
        {
            var size = (aetheryte.Territory.Value?.Map.Value?.SizeFactor ?? 100f) / 100f;
            Location = new Vector2(MarkerToMap(mapMarker.X, size), MarkerToMap(mapMarker.Y, size));

            //static float MarkerToMap(double coord, double scale) => (float)(2 * coord / scale + 100.9);
            static float MarkerToMap(double coord, double scale) => (float)((coord - 1024.0) / scale);
        }
    }

    public void Teleport()
    {
        if (Aetheryte == null)
        {
            Svc.Chat.Print("Invalid target.");
            return;
        }

        if (!IsAttuned) Svc.Chat.Print("Teleport to the unsafe port");
        Telepo.Instance()->Teleport(Aetheryte.RowId, 0);
    }
}
