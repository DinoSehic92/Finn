using Avalonia;
using Avalonia.Media;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Finn.ViewModels
{
    /// <summary>
    /// Lightweight data model for tree view nodes, replacing direct TreeViewItem creation in code-behind.
    /// </summary>
    /// <summary>
    /// Lightweight data model for tree view nodes, replacing direct TreeViewItem creation in code-behind.
    /// </summary>
    public class TreeNodeData : INotifyPropertyChanged
    {
        public string Header { get; init; } = string.Empty;
        public string? BadgeText { get; init; }
        public string Tag { get; init; } = string.Empty;

        /// <summary>
        /// Fluent icon symbol name for the node (e.g. "Folder", "Document").
        /// Bound in XAML via &lt;ic:SymbolIcon Symbol="{Binding IconSymbol}"/&gt;.
        /// </summary>
        public string? IconSymbol { get; init; }

        /// <summary>
        /// For Group and Subgroup nodes: the GroupData.Name this node represents.
        /// Used to look up the GroupData record when context menu actions are invoked.
        /// </summary>
        public string? GroupName { get; init; }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Fluent icon symbol name for the shared sync status badge (e.g. "Checkmark", "ArrowDown").
        /// Null or empty when the node is not a shared project.
        /// </summary>
        public string? SyncIconSymbol { get; init; }

        /// <summary>
        /// Optional tooltip for the sync icon.
        /// </summary>
        public string? SyncTooltip { get; init; }

        /// <summary>
        /// When true, the node represents a shared project the user is viewing (not owning).
        /// </summary>
        public bool IsViewer { get; init; }

        /// <summary>
        /// Optional tooltip for viewer indicator.
        /// </summary>
        public string? ViewerTooltip { get; init; }

        public double FontSize { get; init; } = 14;
        public FontWeight FontWeight { get; init; } = FontWeight.Normal;
        public FontStyle FontStyle { get; init; } = FontStyle.Normal;
        public Color? Foreground { get; init; }

        /// <summary>Extra top margin; used to visually separate category-level nodes.</summary>
        public Thickness NodeMargin { get; init; } = new Thickness(0);

        /// <summary>Override for TreeViewItem MinHeight. 0 means use the global default.</summary>
        public double NodeMinHeight { get; init; } = 0;

        /// <summary>Opacity override for the node's content row (used to de-emphasise filetype children).</summary>
        public double NodeOpacity { get; init; } = 1.0;

        // Returns null when no custom colour is set so the TextBlock.TreeNodeHeader
        // style (which sets AppForegroundBrush via DynamicResource) takes effect.
        // When the user sets a project colour, returns a solid brush for that colour.
        // Note: the XAML binding uses TargetNullValue bound to nothing — the style
        // default is relied upon for the null case.
        public IBrush? ForegroundBrush => Foreground.HasValue ? new SolidColorBrush(Foreground.Value) : null;

        public bool HasCustomForeground => Foreground.HasValue;
        public List<TreeNodeData> Children { get; init; } = [];

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}