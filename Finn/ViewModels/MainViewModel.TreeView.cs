using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Finn.Model;

namespace Finn.ViewModels
{
    // Partial class extension for tree view data building
    public partial class MainViewModel
    {
        private static readonly Color DefaultForeground = Color.Parse("#FFFFFFFF");
        private static readonly string[] CategoryTypes = ["Archive", "Library", "Project"];

        private List<TreeNodeData> _treeNodes = [];
        public List<TreeNodeData> TreeNodes
        {
            get => _treeNodes;
            set => SetProperty(ref _treeNodes, value);
        }

        private TreeNodeData? _selectedTreeNode;
        public TreeNodeData? SelectedTreeNode
        {
            get => _selectedTreeNode;
            set => SetProperty(ref _selectedTreeNode, value);
        }

        /// <summary>
        /// Builds the tree data model. The view binds to <see cref="TreeNodes"/>
        /// via HierarchicalDataTemplate instead of constructing TreeViewItems in code-behind.
        /// </summary>
        public void BuildTreeData()
        {
            GetGroups();
            var nodes = new List<TreeNodeData>();
            TreeNodeData? selectedNode = null;

            foreach (string category in CategoryTypes)
            {
                var projects = Storage.StoredProjects.Where(x => x.Category == category);
                if (!projects.Any())
                    continue;

                var categoryChildren = new List<TreeNodeData>();

                // Top-level projects (no parent group)
                foreach (var project in projects.Where(p => string.IsNullOrEmpty(p.Parent)))
                {
                    var (projectNode, matched) = BuildProjectNodeData(project);
                    categoryChildren.Add(projectNode);
                    selectedNode ??= matched;
                }

                // Grouped projects (only under "Project" category)
                if (category == "Project")
                {
                    foreach (string group in Groups)
                    {
                        var groupedProjects = Storage.StoredProjects.Where(x => x.Parent == group);
                        var groupChildren = new List<TreeNodeData>();

                        foreach (var project in groupedProjects)
                        {
                            var (projectNode, matched) = BuildProjectNodeData(project);
                            groupChildren.Add(projectNode);
                            selectedNode ??= matched;
                        }

                        categoryChildren.Add(new TreeNodeData
                        {
                            Header = group,
                            Tag = "Group",
                            FontSize = 15,
                            FontWeight = FontWeight.Bold,
                            IsExpanded = true,
                            Children = groupChildren
                        });
                    }
                }

                nodes.Add(new TreeNodeData
                {
                    Header = category,
                    Tag = "Header",
                    FontSize = 16,
                    FontWeight = FontWeight.Bold,
                    FontStyle = FontStyle.Italic,
                    IsExpanded = true,
                    Children = categoryChildren
                });
            }

            TreeNodes = nodes;
            SelectedTreeNode = selectedNode;
        }

        private (TreeNodeData node, TreeNodeData? matched) BuildProjectNodeData(ProjectData project)
        {
            TreeNodeData? matched = null;
            var children = new List<TreeNodeData>();
            Color? foreground = project.Foreground != DefaultForeground ? project.Foreground : null;
            bool isCurrent = project == CurrentProject;

            foreach (string filetype in project.StoredFiles.Select(x => x.Filtyp).Distinct())
            {
                int count = project.StoredFiles.Count(x => x.Filtyp == filetype);

                var child = new TreeNodeData
                {
                    Header = $"{filetype}  ({count})",
                    Tag = project.Namn,
                    FontSize = 13,
                    FontWeight = FontWeight.Light,
                    Foreground = foreground
                };

                // Match the filetype node when a file is selected in the current project
                if (isCurrent && Type != null && filetype == Type)
                    matched = child;
                else if (isCurrent && matched == null && CurrentFile?.Filtyp == filetype)
                    matched = child;

                children.Add(child);
            }

            var node = new TreeNodeData
            {
                Header = project.Namn,
                Tag = "All Types",
                FontSize = 15,
                IsExpanded = isCurrent,
                Foreground = foreground,
                Children = children
            };

            // If current project but no specific filetype matched, select the project node itself
            if (isCurrent && matched == null)
                matched = node;

            return (node, matched);
        }
    }
}