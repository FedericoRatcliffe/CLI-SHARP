using CliSharp.Application.Terminal;
using CliSharp.UI.Controls;

namespace CliSharp.UI.Layout;

/// <summary>
/// Binary tree modeling the pane layout in a tab.
/// A node is either a terminal (leaf) or a split (branch with two children).
/// </summary>
public abstract class SplitNode { }

public sealed class TerminalPane : SplitNode, IAsyncDisposable
{
    public required TerminalSession Session { get; init; }
    public required TerminalCanvas Canvas { get; init; }
    public string Title { get; set; } = "PowerShell";

    public async ValueTask DisposeAsync() => await Session.DisposeAsync();
}

public sealed class SplitBranch : SplitNode
{
    public required SplitDirection Direction { get; init; }
    public double Ratio { get; set; } = 0.5;
    public required SplitNode First { get; set; }
    public required SplitNode Second { get; set; }
}

public enum SplitDirection { Horizontal, Vertical }

/// <summary>
/// Utilities for traversing and mutating the split tree.
/// </summary>
public static class SplitTree
{
    public static List<TerminalPane> CollectPanes(SplitNode node)
    {
        var result = new List<TerminalPane>();
        Collect(node, result);
        return result;

        static void Collect(SplitNode n, List<TerminalPane> list)
        {
            if (n is TerminalPane p) list.Add(p);
            else if (n is SplitBranch b) { Collect(b.First, list); Collect(b.Second, list); }
        }
    }

    /// <summary>
    /// Replaces target with replacement in the tree. Returns the new root.
    /// </summary>
    public static SplitNode Replace(SplitNode root, SplitNode target, SplitNode replacement)
    {
        if (root == target) return replacement;
        if (root is SplitBranch b)
        {
            if (b.First == target) { b.First = replacement; return root; }
            if (b.Second == target) { b.Second = replacement; return root; }
            Replace(b.First, target, replacement);
            Replace(b.Second, target, replacement);
        }
        return root;
    }

    /// <summary>
    /// Removes a pane from the tree, promoting its sibling. Returns the new root.
    /// </summary>
    public static SplitNode? Remove(SplitNode root, TerminalPane target)
    {
        if (root == target) return null;
        return RemoveInner(root, target);

        static SplitNode? RemoveInner(SplitNode node, TerminalPane target)
        {
            if (node is not SplitBranch b) return node;

            if (b.First == target) return b.Second;
            if (b.Second == target) return b.First;

            var newFirst = RemoveInner(b.First, target);
            if (newFirst != b.First) { b.First = newFirst!; return b; }

            var newSecond = RemoveInner(b.Second, target);
            if (newSecond != b.Second) { b.Second = newSecond!; return b; }

            return b;
        }
    }
}
