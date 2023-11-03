using KdTree;
using KdTree.Math;
using Roy_T.AStar.Graphs;
using System.Numerics;

namespace TakeMeEverywhere;

internal class PathGraph
{
    public IEnumerable<INode> Nodes => _tree.Select(t => t.Value);

    private readonly KdTree<float, INode> _tree = new(3, new FloatMath());

    public void Load(IEnumerable<INode> nodes)
    {
        foreach (var node in nodes)
        {
            _tree.Add(GetPt(node.Position), node);
        }
    }

    public void Add(INode node, params INode[] connectedNodes)
    {
        if (!_tree.Add(GetPt(node.Position), node)) return;

        foreach (var connectedNode in connectedNodes)
        {
            connectedNode.Connect(node, 1);
            node.Connect(connectedNode, 1);
        }
    }

    public INode? GetClosest(Vector3 position, float maxDistance = 1.5f)
    {
        var nodes = _tree.GetNearestNeighbours(GetPt(position), 1);
        if (nodes == null || nodes.Length == 0) return null;
        var node = nodes[0].Value;

        return (node.Position - position).LengthSquared() > maxDistance * maxDistance 
            ? null : node;
    }

    public void Clear()
    {
        _tree.Clear();
    }

    public void Remove(INode node)
    {
        _tree.RemoveAt(GetPt(node.Position));

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
    }

    private static float[] GetPt(Vector3 pt) => new float[] { pt.X, pt.Y, pt.Z };
}
