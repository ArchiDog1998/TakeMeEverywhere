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
    public static PathGraph RunNodes { get; private set; } = new();
    public static PathGraph FlyNodes { get; private set; } = new();

    private class RunNodesDrawing : NodesDrawing
    {
        public override IEnumerable<INode> Nodes => RunNodes.Nodes;
    }

    private class FlyNodesDrawing : NodesDrawing
    {
        public override IEnumerable<INode> Nodes => FlyNodes.Nodes;
    }

    public static DesiredPosition? Position { get; set; }

    public static void Init(DalamudPluginInterface pluginInterface)
    {
        Painter = XIVPainter.XIVPainter.Create(pluginInterface, "Take Me Everywhere");
        Runner = XIVRunner.XIVRunner.Create(pluginInterface);
        Runner.Enable = true;
        Runner.RunFastAction = RunFast;

        var cir = new Drawing3DCircularSector(default, 0, ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.9f, 0.1f, 0.7f)), 2)
        {
            DrawWithHeight = false,
        };

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

        SelectedNode = XIVRunner.XIVRunner.IsFlying 
            ? FlyNodes.GetClosest(pos)
            : RunNodes.GetClosest(pos);

        if (SelectedNode != null) return;

        var node = new Node(pos);

        if (XIVRunner.XIVRunner.IsFlying)
        {
            FlyNodes.Add(node);
        }
        else
        {
            RunNodes.Add(node);
        }

        SelectedNode = node;
    }

    public static void DeleteNode()
    {
        if (!Player.Available) return;
        var pos = Player.Object.Position;

        var node = RunNodes.GetClosest(pos);
        if (node != null)
        {
            RunNodes.Remove(node);
        }
        else
        {
            node = FlyNodes.GetClosest(pos);
            if(node != null)
            {
                FlyNodes.Remove(node);
            }
        }

        if (node == null) return;

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
        if (FlyNodes.GetClosest(SelectedNode.Position) != null
            && XIVRunner.XIVRunner.IsFlying)
        {
            node = FlyNodes.GetClosest(pos) ?? new Node(pos);
            FlyNodes.Add(node, SelectedNode);
        } 
        else if (RunNodes.GetClosest(SelectedNode.Position) != null
            && !XIVRunner.XIVRunner.IsFlying)
        {
            node = RunNodes.GetClosest(pos) ?? new Node(pos);
            RunNodes.Add(node, SelectedNode);
        }
        else
        {
            return;
        }

        SelectedNode = node;
    }

    public static void DisconnectNode()
    {
        if (SelectedNode == null) return;
        if (!Player.Available) return;
        var pos = Player.Object.Position;

        INode? node = null;

        if (FlyNodes.GetClosest(SelectedNode.Position) != null)
        {
            node = FlyNodes.GetClosest(pos);
        }
        else if (RunNodes.GetClosest(SelectedNode.Position) != null)
        {
            node = RunNodes.GetClosest(pos);
        }

        if (node == null) return;

        SelectedNode.Disconnect(node);
        node.Disconnect(SelectedNode);
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
            Run = new GraphDto(RunNodes.Nodes),
            Fly = new GraphDto(FlyNodes.Nodes),
        }, Formatting.Indented);

        await File.WriteAllTextAsync(file, str);
    }

    private static async void TerritoryChanged(ushort obj)
    {
        Runner.NaviPts.Clear();
        RunNodes.Clear();
        FlyNodes.Clear();

        var name = Svc.Data.GetExcelSheet<TerritoryType>()?.GetRow(obj)?.Name.RawString;
        if (string.IsNullOrEmpty(name))
        {
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
                return;
            }
            if (!File.Exists(file))
            {
                return;
            }
        }

        var str = await File.ReadAllTextAsync(file);
        try
        {
            var graph = JsonConvert.DeserializeObject<TerritoryGraph>(str);
            RunNodes.Load( graph.Run.ToNodes());
            FlyNodes.Load(graph.Fly.ToNodes());
        }
        catch (Exception ex)
        {
            Svc.Log.Information(ex, "Failed to load territory graph.");
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
    public abstract IEnumerable<INode> Nodes { get; }

    private static readonly uint color = uint.MaxValue;

    public override void UpdateOnFrame(XIVPainter.XIVPainter painter)
    {
        SubItems = Array.Empty<IDrawing3D>();

        if (!TakeMeEverywherePlugin.IsOpen) return;

        if(!Player.Available) return;
        var playerPosition = Player.Object.Position;

        var result = new List<IDrawing3D>();
        foreach (var node in Nodes)
        {
            var pos = node.Position;
            if ((pos - playerPosition).LengthSquared() > 2500) continue;

            result.Add(new Drawing3DCircularSector(pos, 0.1f, color, 1)
            {
                DrawWithHeight = false,
            });
            foreach (var outgoing in node.Outgoing)
            {
                result.Add(new Drawing3DPolyline(new Vector3[] { pos, outgoing.End.Position }, color, 1)
                {
                    IsFill = false,
                    DrawWithHeight = false,
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
                result.Add(new(lastPosition, pt, 0.5f, color, 1)
                {
                    DrawWithHeight = false,
                });
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

            result.Add(new Drawing3DText(aetheryte.Name, pos)
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