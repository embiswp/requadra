using System.Text;

namespace Quadra.Core.Qserv;

/// <summary>
/// The key/value tree Qserv messages are made of: one <c>path/to/key value</c> line per leaf, where the
/// path is split on '/' into nested nodes. Ports <c>parse</c>, <c>set_param</c> and <c>format_params</c>
/// from legacy/server/qserv.pl and the client's <c>Dict</c>.
/// </summary>
public sealed class QservTree
{
    private readonly Dictionary<string, object> _items = [];

    public int Count => _items.Count;

    public IEnumerable<string> Keys => _items.Keys;

    /// <summary>Sets a leaf, creating the nodes on its path. A leaf in the way is replaced by a node.</summary>
    public void Set(string path, string value)
    {
        var parts = path.Split('/');
        var node = this;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (node._items.TryGetValue(parts[i], out var next) && next is QservTree sub)
                node = sub;
            else
            {
                sub = new QservTree();
                node._items[parts[i]] = sub;
                node = sub;
            }
        }
        node._items[parts[^1]] = value;
    }

    /// <summary>The leaf at <paramref name="path"/>, or null.</summary>
    public string? Get(string path)
    {
        var node = this;
        var parts = path.Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!node._items.TryGetValue(parts[i], out var next) || next is not QservTree sub)
                return null;
            node = sub;
        }
        return node._items.TryGetValue(parts[^1], out var leaf) ? leaf as string : null;
    }

    /// <summary>The node at <paramref name="path"/>, or null.</summary>
    public QservTree? Sub(string path)
    {
        var node = this;
        foreach (var part in path.Split('/'))
        {
            if (!node._items.TryGetValue(part, out var next) || next is not QservTree sub)
                return null;
            node = sub;
        }
        return node;
    }

    /// <summary>The child nodes in insertion order, with their keys.</summary>
    public IEnumerable<KeyValuePair<string, QservTree>> Nodes() =>
        _items.Where(kv => kv.Value is QservTree).Select(kv => KeyValuePair.Create(kv.Key, (QservTree)kv.Value));

    public bool Remove(string key) => _items.Remove(key);

    public QservTree Clone()
    {
        var copy = new QservTree();
        foreach (var (key, value) in _items)
            copy._items[key] = value is QservTree sub ? sub.Clone() : value;
        return copy;
    }

    /// <summary>
    /// Parses lines. A line is a key, then the first whitespace character, then the value (empty when
    /// there is none). Blank lines are skipped.
    /// </summary>
    public static QservTree Parse(IEnumerable<string> lines)
    {
        var tree = new QservTree();
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
                continue;
            int split = line.IndexOfAny([' ', '\t']);
            if (split < 0)
                tree.Set(line, "");
            else
                tree.Set(line[..split], line[(split + 1)..]);
        }
        return tree;
    }

    /// <summary>Writes the tree as lines, each key prefixed with <paramref name="root"/>/ when given.</summary>
    public string Format(string? root = null)
    {
        var sb = new StringBuilder();
        Format(sb, root);
        return sb.ToString();
    }

    private void Format(StringBuilder sb, string? root)
    {
        foreach (var (key, value) in _items)
        {
            var path = root is null ? key : root + "/" + key;
            if (value is QservTree sub)
                sub.Format(sb, path);
            else
                sb.Append(path).Append(' ').Append((string)value).Append('\n');
        }
    }
}
