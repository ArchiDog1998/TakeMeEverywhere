using Dalamud.Plugin;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.GeneratedSheets;
using Newtonsoft.Json;
using Roy_T.AStar.Graphs;
using System.Numerics;
using XIVPainter.Element3D;

namespace TakeMeEverywhere;

internal static class Service
{
    public static XIVPainter.XIVPainter Painter { get; private set; } = null!;
    public static XIVRunner.XIVRunner Runner { get; private set; } = null!;

    public static INode[] RunNodes { get; private set; } = Array.Empty<INode>();
    public static INode[] FlyNodes { get; private set; } = Array.Empty<INode>();

    public static DesiredPosition? Position { get; set; }

    private static readonly NodesDrawing _nodeDrawing = new ();
    public static void Init(DalamudPluginInterface pluginInterface)
    {
        Painter = XIVPainter.XIVPainter.Create(pluginInterface, "Take Me Everywhere");
        Runner = XIVRunner.XIVRunner.Create(pluginInterface);
        Runner.Enable = true;

        Painter.AddDrawings(_nodeDrawing, new PathDrawing(), new AetheryteDrawing());

        var aetheries = Svc.Data.GetExcelSheet<Aetheryte>();

        Svc.ClientState.TerritoryChanged += TerritoryChanged;
        TerritoryChanged(Svc.ClientState.TerritoryType);
    }

    public static void Dispose()
    {
        Painter.Dispose();
        Runner.Dispose();

        Svc.ClientState.TerritoryChanged -= TerritoryChanged;
    }

    private static async void TerritoryChanged(ushort obj)
    {
        Runner.NaviPts.Clear();

        var name = Svc.Data.GetExcelSheet<TerritoryType>()?.GetRow(obj)?.Name.RawString;
        if (string.IsNullOrEmpty(name))
        {
            RunNodes = Array.Empty<INode>();
            return;
        }

        var file = GetFile(name);
        if (!File.Exists(file))
        {
            try
            {
                await DownloadFile(name, file);
            }
            catch (Exception ex)
            {
                Svc.Log.Information(ex, "Failed to download territory graph.");
                RunNodes = FlyNodes = Array.Empty<INode>();
                return;
            }
            if (!File.Exists(file))
            {
                RunNodes = FlyNodes = Array.Empty<INode>();
                return;
            }
        }

        var str = await File.ReadAllTextAsync(file);
        try
        {
            var graph = JsonConvert.DeserializeObject<TerritoryGraph>(str);
            RunNodes = graph.Run.ToNodes();
            FlyNodes = graph.Fly.ToNodes();

            _nodeDrawing.Nodes = RunNodes;
        }
        catch (Exception ex)
        {
            Svc.Log.Information(ex, "Failed to load territory graph.");
            RunNodes = FlyNodes = Array.Empty<INode>();
        }

        static string GetFile(string name)
        {
            var dirInfo = Svc.PluginInterface.ConfigDirectory;

            if (!dirInfo.Exists)
            {
                dirInfo.Create();
            }

            return Path.Combine(dirInfo.FullName, name + ".json");
        }

        static async Task DownloadFile(string name, string file)
        {
            using var client = new HttpClient();
            var str = await client.GetStringAsync($"https://raw.githubusercontent.com/ArchiDog1998/TakeMeEverywhere/main/TerritoryMesh/{name}.json");

            await File.WriteAllTextAsync(file, str);
        }
    }
}

internal class NodesDrawing : Drawing3DPoly
{
    public INode[]? Nodes { get; set; }

    public override void UpdateOnFrame(XIVPainter.XIVPainter painter)
    {
        SubItems = Array.Empty<IDrawing3D>();

        if (Nodes == null) return;

        if(!Player.Available) return;
        var playerPosition = Player.Object.Position;

        var result = new List<Drawing3DPolyline>();
        foreach (var node in Nodes)
        {
            var pos = node.Position;
            if ((pos - playerPosition).LengthSquared() > 2500) continue;

            foreach (var outgoing in node.Outgoing)
            {
                result.Add(new(new Vector3[] { pos, outgoing.End.Position }, uint.MaxValue, 1)
                {
                    IsFill = false,
                });
            }
        }
        SubItems = result.ToArray();

        base.UpdateOnFrame(painter);
    }
}

internal class AetheryteDrawing : Drawing3DPoly
{
    public override void UpdateOnFrame(XIVPainter.XIVPainter painter)
    {
        SubItems = Array.Empty<IDrawing3D>();

        if (!Player.Available) return;
        var playerPosition = Player.Object.Position;

        var result = new List<Drawing3DCircularSector>();

        foreach (var aetheryte in AetheryteInfo.AetheryteInfos)
        {
            if (aetheryte.Aetheryte.Territory.Row != Svc.ClientState.TerritoryType) continue;

            var pos = new Vector3(aetheryte.Location.X, 0, aetheryte.Location.Y);
            if ((pos - playerPosition).LengthSquared() > 2500) continue;

            result.Add(new Drawing3DCircularSector(pos, 0.1f, uint.MaxValue, 1));
        }

        SubItems = result.ToArray();
        base.UpdateOnFrame(painter);
    }
}

internal class PathDrawing : Drawing3DPoly
{
    public override void UpdateOnFrame(XIVPainter.XIVPainter painter)
    {
        SubItems = Array.Empty<IDrawing3D>();

        if (!Player.Available) return;
        var lastPosition = Player.Object.Position;

        if (Service.Runner.NaviPts.Count == 0) return;

        var result = new List<Drawing3DHighlightLine>();
        foreach (var pt in Service.Runner.NaviPts)
        {
            result.Add(new(lastPosition, pt, 0.5f, uint.MaxValue, 1));
            lastPosition = pt;
        }

        SubItems = result.ToArray();
        base.UpdateOnFrame(painter);
    }
}