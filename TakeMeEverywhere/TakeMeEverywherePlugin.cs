using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Lumina.Excel.GeneratedSheets;
using Newtonsoft.Json;
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

    readonly XIVPainter.XIVPainter _painter;
    readonly XIVRunner.XIVRunner _runner;
    //readonly IDalamudPlugin _lifeStream;

    readonly AetheryteInfo[] _aetheryteInfos;

    private INode[] _runNodes = Array.Empty<INode>(), _flyNodes = Array.Empty<INode>();

    public DesiredPosition? Position { get; set; }

    public TakeMeEverywherePlugin(DalamudPluginInterface pluginInterface)
    {
        ECommonsMain.Init(pluginInterface, this, Module.DalamudReflector, Module.ObjectFunctions);
        _painter = XIVPainter.XIVPainter.Create(pluginInterface, "Take Me Everywhere");
        _runner = XIVRunner.XIVRunner.Create(pluginInterface);

        _aetheryteInfos = Svc.Data.GetExcelSheet<Aetheryte>()?
            .Select(a => new AetheryteInfo(a)).ToArray()
            ?? Array.Empty<AetheryteInfo>();

        //if(!DalamudReflector.TryGetDalamudPlugin("Lifestream", out _lifeStream))
        //{
        //    Svc.Chat.Print("Failed to get lifestream");
        //}

        Svc.Framework.Update += Update;
        Svc.ClientState.TerritoryChanged += TerritoryChanged;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= Update;
        Svc.ClientState.TerritoryChanged -= TerritoryChanged;

        _painter.Dispose();
        _runner.Dispose();
        ECommonsMain.Dispose();
    }

    private async void TerritoryChanged(ushort obj)
    {
        _runner.NaviPts.Clear();

        var name = Svc.Data.GetExcelSheet<TerritoryType>()?.GetRow(obj)?.Name.RawString;
        if(string.IsNullOrEmpty(name))
        {
            _runNodes = Array.Empty<INode>();
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
                _runNodes = _flyNodes = Array.Empty<INode>();
                return;
            }
            if (!File.Exists(file))
            {
                _runNodes = _flyNodes = Array.Empty<INode>();
                return;
            }
        }

        var str = await File.ReadAllTextAsync(file);
        try
        {
            var graph = JsonConvert.DeserializeObject<TerritoryGraph>(str);
            _runNodes = graph.Run.ToNodes();
            _flyNodes = graph.Fly.ToNodes();
        }
        catch (Exception ex)
        {
            Svc.Log.Information(ex, "Failed to load territory graph.");
            _runNodes = _flyNodes = Array.Empty<INode>();
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

    private void Update(IFramework framework)
    {
#if DEBUG
        Position = DesiredPosition.FromFlag();
#endif

        Position?.CheckYValue();

        if (Position == null) return;

        CheckTeleport(Position.Value);
        RunToPotision(Position.Value);
    }

    private void CheckTeleport(DesiredPosition position)
    {
        if (Svc.ClientState.TerritoryType == position.TerritoryId) return;

        var pt = new Vector2(position.Position.X, position.Position.Z);

        var aetheryte = _aetheryteInfos
            .Where(a => a.Aetheryte.Territory.Row == position.TerritoryId)
            .OrderBy(a => (a.Location - pt).LengthSquared())
            .FirstOrDefault();

        //TODO: Better safe teleport!
        aetheryte.Teleport();
    }

    private PathState _state = PathState.None;
    private void RunToPotision(DesiredPosition position)
    {
        var hasNaviPts = _runner.NaviPts.Count > 0;

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
            < _runner.Precision * _runner.Precision)
            {
                Position = null;
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
                FindGraphWithNodes(position, _runNodes);
                _state = PathState.Run;
                return;

            case PathState.Run:
                FindGraphWithNodes(position, _flyNodes);
                _state = PathState.Fly;
                return;
        }
    }

    private void FindGraphWithNodes(DesiredPosition position, INode[] nodes)
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
            _runner.NaviPts.Enqueue(edge.End.Position);
        }
        _runner.NaviPts.Enqueue(end);
    }
}
