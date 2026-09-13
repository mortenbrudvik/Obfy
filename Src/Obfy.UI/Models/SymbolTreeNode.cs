using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Obfy.UI.Models;

/// <summary>
/// Represents the type of symbol in the results tree.
/// </summary>
public enum SymbolType
{
    Namespace,
    Type,
    Method,
    Field,
    Property,
    Parameter
}

/// <summary>
/// Represents a node in the symbol mapping tree view.
/// </summary>
public partial class SymbolTreeNode : ObservableObject
{
    [ObservableProperty]
    private string _originalName = string.Empty;

    [ObservableProperty]
    private string _obfuscatedName = string.Empty;

    [ObservableProperty]
    private SymbolType _symbolType;

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Gets the child nodes of this symbol.
    /// </summary>
    public ObservableCollection<SymbolTreeNode> Children { get; } = new();

    public override string ToString() => $"{OriginalName} -> {ObfuscatedName}";

    /// <summary>
    /// Gets whether this node has children.
    /// </summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>
    /// Creates a new symbol tree node.
    /// </summary>
    public static SymbolTreeNode Create(string originalName, string obfuscatedName, SymbolType symbolType)
    {
        return new SymbolTreeNode
        {
            OriginalName = originalName,
            ObfuscatedName = obfuscatedName,
            SymbolType = symbolType
        };
    }

    /// <summary>
    /// Adds a child node to this node.
    /// </summary>
    public void AddChild(SymbolTreeNode child)
    {
        Children.Add(child);
    }
}
