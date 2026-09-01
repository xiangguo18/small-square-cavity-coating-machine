using Opc.Ua;

namespace Small_square_cavity_coating_machine.Services.Alarms;

public sealed record AlarmBrowseNode(NodeId Id, string BrowseName, NodeClass NodeClass);
public interface IAlarmNodeBrowser
{
    Task<IReadOnlyList<AlarmBrowseNode>> BrowseAsync(NodeId parent, CancellationToken token);
    Task<bool> IsReadableArrayAsync(NodeId node, CancellationToken token);
}

public static class AlarmArrayDiscovery
{
    public static async Task<NodeId> FindAsync(IAlarmNodeBrowser browser, string name, CancellationToken token)
    {
        var visited = new HashSet<NodeId> { ObjectIds.ObjectsFolder };
        var queue = new Queue<NodeId>();
        queue.Enqueue(ObjectIds.ObjectsFolder);
        var matches = new List<NodeId>();
        while (queue.TryDequeue(out var parent))
        {
            token.ThrowIfCancellationRequested();
            foreach (var node in await browser.BrowseAsync(parent, token).ConfigureAwait(false))
            {
                if (!visited.Add(node.Id)) continue;
                if (visited.Count > 50000) throw new InvalidOperationException("节点浏览超过50000项，无法确认唯一报警数组");
                if (node.NodeClass == NodeClass.Variable && node.BrowseName == name
                    && await browser.IsReadableArrayAsync(node.Id, token).ConfigureAwait(false))
                    matches.Add(node.Id);
                if (node.NodeClass is NodeClass.Object or NodeClass.Variable) queue.Enqueue(node.Id);
            }
        }
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"未找到可读报警数组 {name}，请核对PLC变量发布设置"),
            _ => throw new InvalidOperationException($"找到多个同名报警数组 {name}：{string.Join(", ", matches)}；禁止猜测绑定")
        };
    }
}
