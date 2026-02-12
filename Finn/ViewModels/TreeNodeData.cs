using Avalonia.Media;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Finn.ViewModels
{
    /// <summary>
    /// Lightweight data model for tree view nodes, replacing direct TreeViewItem creation in code-behind.
    /// </summary>
    public class TreeNodeData : INotifyPropertyChanged
    {
        public string Header { get; init; } = string.Empty;
        public string Tag { get; init; } = string.Empty;

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

        public double FontSize { get; init; } = 14;
        public FontWeight FontWeight { get; init; } = FontWeight.Normal;
        public FontStyle FontStyle { get; init; } = FontStyle.Normal;
        public Color? Foreground { get; init; }
        public List<TreeNodeData> Children { get; init; } = [];

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}