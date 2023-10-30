using Dalamud.Plugin;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using Newtonsoft.Json;
using Roy_T.AStar.Graphs;
using Roy_T.AStar.Serialization;
using System.Numerics;
using XIVPainter;
using XIVPainter.Element3D;

namespace TakeMeEverywhere;

internal static class Service
{
    public static XIVPainter.XIVPainter Painter { get; private set; } = null!;
    public static XIVRunner.XIVRunner Runner { get; private set; } = null!;

    public static INode? SelectedNode { get; private set; } = null;
    public static INode[] RunNodes { get; private set; } = Array.Empty<INode>();
    public static INode[] FlyNodes { get; private set; } = Array.Empty<INode>();

    public static INode[] SelectedNodes
    {
        get
        {
            if (SelectedNodes == null) return Array.Empty<INode>();
            if (RunNodes.Contains(SelectedNode)) return RunNodes;
            if (FlyNodes.Contains(SelectedNode)) return FlyNodes;
            return Array.Empty<INode>();
        }
        set
        {
            if (SelectedNodes == null) return;
            if (RunNodes.Contains(SelectedNode))
            {
                RunNodes = value;
            }
            else if (FlyNodes.Contains(SelectedNode))
            {
                FlyNodes = value;
            }
        }
    }

    private class RunNodesDrawing : NodesDrawing
    {
        public override INode[]? Nodes => RunNodes;
    }

    private class FlyNodesDrawing : NodesDrawing
    {
        public override INode[]? Nodes => FlyNodes;
    }

    public static DesiredPosition? Position { get; set; }

    public static void Init(DalamudPluginInterface pluginInterface)
    {
        Painter = XIVPainter.XIVPainter.Create(pluginInterface, "Take Me Everywhere");
        Runner = XIVRunner.XIVRunner.Create(pluginInterface);
        Runner.Enable = true;
        Runner.RunFastAction = RunFast;

        var cir = new Drawing3DCircularSector(default, 0, ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.9f, 0.1f, 0.7f)), 2);

        cir.UpdateEveryFrame = () =>
        {
            cir.Center = SelectedNode?.Position ?? default;

            var d = DateTime.Now.Millisecond / 1000f;
            var ratio = (float)DrawingExtensions.EaseFuncRemap(EaseFuncType.None, EaseFuncType.Cubic)(d);
            cir.Radius = SelectedNode == null || !TakeMeEverywherePlugin.IsOpen ? 0 : ratio * 0.5f;
        };

        Painter.AddDrawings(new RunNodesDrawing(), new FlyNodesDrawing(), new PathDrawing(), cir);

#if DEBUG
        Painter.AddDrawings(new AetheryteDrawing());
#endif

        Svc.ClientState.TerritoryChanged += TerritoryChanged;
        TerritoryChanged(Svc.ClientState.TerritoryType);
    }

    private static void RunFast()
    {
        //Something want to use for running fast!
    }

    public static void Dispose()
    {
        Painter.Dispose();
        Runner.Dispose();

        Svc.ClientState.TerritoryChanged -= TerritoryChanged;
        SaveTerritoryGraph();
    }

    public static void SelectOrAddNode()
    {
        if (!Player.Available) return;
        var pos = Player.Object.Position;

        SelectedNode = GetClosestNodeFromNodes(RunNodes, pos)
            ?? GetClosestNodeFromNodes(FlyNodes, pos);

        if (SelectedNode != null) return;

        var node = new Node(pos);

        if (XIVRunner.XIVRunner.IsFlying)
        {
            FlyNodes = FlyNodes.Append(node).ToArray();
        }
        else
        {
            RunNodes = RunNodes.Append(node).ToArray();
        }
    }

    public static void DeleteNode()
    {
        if (!Player.Available) return;
        var pos = Player.Object.Position;

        var node = GetClosestNodeFromNodes(RunNodes, pos);
        if (node != null)
        {
            var list = RunNodes.ToList();
            list.Remove(node);
            RunNodes = list.ToArray();
        }
        else
        {
            node = GetClosestNodeFromNodes(FlyNodes, pos);
            if(node != null)
            {
                var list = FlyNodes.ToList();
                list.Remove(node);
                FlyNodes = list.ToArray();
            }
        }

        if (node == null) return;

        foreach (var item in node.Outgoing)
        {
            item.End.Disconnect(node);
            node.Disconnect(item.End);
        }

        foreach (var item in node.Incoming)
        {
            item.Start.Disconnect(node);
            node.Disconnect(item.Start);
        }

        if(node == SelectedNode)
        {
            SelectedNode = null;
        }
    }

    public static void ConnectNode()
    {
        if (SelectedNode == null) return;
        if (!Player.Available) return;
        var pos = Player.Object.Position;

        INode node;
        if (FlyNodes.Contains(SelectedNode))
        {
            node = GetClosestNodeFromNodes(FlyNodes, pos) ?? new Node(pos);
            FlyNodes = FlyNodes.Append(node).ToArray();
        } 
        else if (RunNodes.Contains(SelectedNode))
        {
            node = GetClosestNodeFromNodes(RunNodes, pos) ?? new Node(pos);
            RunNodes = RunNodes.Append(node).ToArray();
        }
        else
        {
            return;
        }

        SelectedNode.Connect(node, 1);
        node.Connect(SelectedNode, 1);

        SelectedNode = node;
    }

    public static void DisconnectNode()
    {
        if (SelectedNode == null) return;
        if (!Player.Available) return;
        var pos = Player.Object.Position;

        INode? node = null;

        if (FlyNodes.Contains(SelectedNode))
        {
            node = GetClosestNodeFromNodes(FlyNodes, pos);
        }
        else if (RunNodes.Contains(SelectedNode))
        {
            node = GetClosestNodeFromNodes(RunNodes, pos);
        }

        if (node == null) return;

        SelectedNode.Disconnect(node);
        node.Disconnect(SelectedNode);
    }

    private static INode? GetClosestNodeFromNodes(IEnumerable<INode>? nodes, Vector3 pos)
    {
        var node = nodes?.MinBy(n => (n.Position - pos).LengthSquared());
        if (node == null) return null;
        if ((node.Position - pos).LengthSquared() > 1) return null;

        return node;
    }

    public static async void SaveTerritoryGraph()
    {
        var id = Svc.ClientState.TerritoryType;
        var name = Svc.Data.GetExcelSheet<TerritoryType>()?.GetRow(id)?.Name.RawString;

        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var file = GetFile(name);

        var str = JsonConvert.SerializeObject(new TerritoryGraph()
        {
            Run = new GraphDto(RunNodes),
            Fly = new GraphDto(FlyNodes),
        }, Formatting.Indented);

        await File.WriteAllTextAsync(file, str);
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
        }
        catch (Exception ex)
        {
            Svc.Log.Information(ex, "Failed to load territory graph.");
            RunNodes = FlyNodes = Array.Empty<INode>();
        }

        static async Task DownloadFile(string name, string file)
        {
            using var client = new HttpClient();
            var str = await client.GetStringAsync($"https://raw.githubusercontent.com/ArchiDog1998/TakeMeEverywhere/main/TerritoryMesh/{name}.json");

            await File.WriteAllTextAsync(file, str);
        }
    }

    private static string GetFile(string name)
    {
        var dir = Svc.PluginInterface.ConfigDirectory.FullName;

#if DEBUG
        var locDir = @"E:\OneDrive - stu.zafu.edu.cn\PartTime\FFXIV\TakeMeEverywhere\TerritoryMesh";
        if(Directory.Exists(locDir))
        {
            dir = locDir;
        }
#endif
       
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        return Path.Combine(dir, name + ".json");
    }
}

internal abstract class NodesDrawing : Drawing3DPoly
{
    public abstract INode[]? Nodes { get; }

    private static readonly uint color = uint.MaxValue;

    public override void UpdateOnFrame(XIVPainter.XIVPainter painter)
    {
        SubItems = Array.Empty<IDrawing3D>();

        if (Nodes == null) return;
        if (!TakeMeEverywherePlugin.IsOpen) return;

        if(!Player.Available) return;
        var playerPosition = Player.Object.Position;

        var result = new List<IDrawing3D>();
        foreach (var node in Nodes)
        {
            var pos = node.Position;
            if ((pos - playerPosition).LengthSquared() > 2500) continue;

            result.Add(new Drawing3DCircularSector(pos, 0.1f, color, 1));
            foreach (var outgoing in node.Outgoing)
            {
                result.Add(new Drawing3DPolyline(new Vector3[] { pos, outgoing.End.Position }, color, 1)
                {
                    IsFill = false,
                });
            }
        }
        SubItems = result.ToArray();

        base.UpdateOnFrame(painter);
    }
}

internal class PathDrawing : Drawing3DPoly
{
    private static readonly uint color = ImGui.ColorConvertFloat4ToU32(new Vector4(0, 1f, 0.8f, 0.6f));

    public override void UpdateOnFrame(XIVPainter.XIVPainter painter)
    {
        SubItems = Array.Empty<IDrawing3D>();

        if (!Player.Available) return;
        var playerPosition = Player.Object.Position;
        var lastPosition = playerPosition;

        if (Service.Runner.NaviPts.Count == 0) return;

        var result = new List<Drawing3DHighlightLine>();
        foreach (var pt in Service.Runner.NaviPts)
        {
            if ((pt - playerPosition).LengthSquared() <= 2500)
            {
                result.Add(new(lastPosition, pt, 0.5f, color, 1));
            }

            lastPosition = pt;
        }

        SubItems = result.ToArray();
        base.UpdateOnFrame(painter);
    }
}

#if DEBUG
internal class AetheryteDrawing : Drawing3DPoly
{
    public override void UpdateOnFrame(XIVPainter.XIVPainter painter)
    {
        SubItems = Array.Empty<IDrawing3D>();

        if (!Player.Available) return;
        var playerPosition = Player.Object.Position;

        var result = new List<IDrawing3D>();

        foreach (var aetheryte in AetheryteInfo.AetheryteInfos)
        {
            if (aetheryte.Aetheryte.Territory.Row != Svc.ClientState.TerritoryType) continue;

            var pos = new Vector3(aetheryte.Location.X, 0, aetheryte.Location.Y);
            if ((pos - playerPosition).LengthSquared() > 2500) continue;

            result.Add(new Drawing3DCircularSector(pos, 0.1f, uint.MaxValue, 1));

            result.Add(new Drawing3DText($"{aetheryte.Aetheryte.PlaceName.Value?.Name.RawString ?? "Place"} - {aetheryte.Aetheryte.AethernetName.Value?.Name.RawString?? string.Empty}", pos)
            {
                Color = uint.MaxValue,
                DrawWithHeight = false,
            });
        }

        SubItems = result.ToArray();
        base.UpdateOnFrame(painter);
    }
}
#endif