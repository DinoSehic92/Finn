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
        /// Maps a filetype name to a Fluent icon symbol for the tree view.
        /// </summary>
        private static string GetFiletypeIcon(string filetype) => filetype switch
        {
            "PDF"         => "DocumentPdf",
            "Drawing"     => "PaintBrush",
            "Document"    => "DocumentText",
            "New"         => "DocumentAdd",
            "Other Files" or "Other" => "DocumentQuestionMark",
            _             => "Document"
        };

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
                            IconSymbol = "Album",
                            FontSize = 15,
                            FontWeight = FontWeight.Bold,
                            IsExpanded = true,
                            Children = groupChildren
                        });
                    }
                }

                string categoryIcon = category switch
                {
                    "Archive" => "Archive",
                    "Library" => "Library",
                    _ => "Briefcase"
                };

                nodes.Add(new TreeNodeData
                {
                    Header = category,
                    Tag = "Header",
                    IconSymbol = categoryIcon,
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

        /// <summary>
        /// Lightweight navigation that walks the existing tree nodes to update
        /// <see cref="SelectedTreeNode"/> and <see cref="TreeNodeData.IsExpanded"/>
        /// without rebuilding the tree. Use this when only the current project or
        /// filetype selection changed (e.g. selecting a recent file).
        /// Falls back to <see cref="BuildTreeData"/> when the tree has not been built yet.
        /// </summary>
        public void NavigateTreeToCurrentProject()
        {
            if (TreeNodes.Count == 0)
            {
                BuildTreeData();
                return;
            }

            TreeNodeData? selectedNode = null;

            foreach (var categoryNode in TreeNodes)
            {
                foreach (var child in categoryNode.Children)
                {
                    if (child.Tag == "Group")
                    {
                        foreach (var projectNode in child.Children)
                            selectedNode ??= TrySelectProjectNode(projectNode);
                    }
                    else
                    {
                        selectedNode ??= TrySelectProjectNode(child);
                    }
                }
            }

            if (selectedNode != null)
                SelectedTreeNode = selectedNode;
        }

        /// <summary>
        /// Checks whether <paramref name="projectNode"/> matches <see cref="CurrentProject"/>.
        /// If it does, expands the node and returns the best-matching child (or the node itself).
        /// Non-matching project nodes are collapsed.
        /// </summary>
        private TreeNodeData? TrySelectProjectNode(TreeNodeData projectNode)
        {
            if (projectNode.Tag != "All Types" || projectNode.Header.Split("  ")[0] != CurrentProject?.Namn)
            {
                projectNode.IsExpanded = false;
                return null;
            }

            projectNode.IsExpanded = true;

            // Try to match a specific filetype child
            string? targetType = Type is not null and not "All Types" ? Type : CurrentFile?.Filtyp;

            if (targetType != null)
            {
                foreach (var child in projectNode.Children)
                {
                    string childFiletype = child.Header.Split("  ")[0];
                    if (childFiletype == targetType)
                        return child;
                }
            }

            return projectNode;
        }

        private (TreeNodeData node, TreeNodeData? matched) BuildProjectNodeData(ProjectData project)
        {
            TreeNodeData? matched = null;
            var children = new List<TreeNodeData>();
            Color? foreground = project.Foreground != DefaultForeground ? project.Foreground : null;
            bool isCurrent = project == CurrentProject;
            var topLevel = project.StoredFiles.Where(f => !f.IsAppendedFile);

            foreach (string filetype in topLevel.Select(x => x.Filtyp).Distinct())
            {
                int count = topLevel.Count(x => x.Filtyp == filetype);

                var child = new TreeNodeData
                {
                    Header = $"{filetype}  ({count})",
                    Tag = project.Namn,
                    IconSymbol = GetFiletypeIcon(filetype),
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
                IconSymbol = "Folder",
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