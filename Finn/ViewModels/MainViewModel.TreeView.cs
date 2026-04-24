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
                var projects = Storage.StoredProjects.Where(x => x.Category == category).ToList();
                var groups = Storage.ProjectGroups.Where(g => g.Category == category).ToList();
                if (projects.Count == 0 && groups.Count == 0) continue;

                var categoryChildren = new List<TreeNodeData>();

                // Top-level projects (no parent group)
                foreach (var project in projects.Where(p => string.IsNullOrEmpty(p.Parent)))
                {
                    var (projectNode, matched) = BuildProjectNodeData(project);
                    categoryChildren.Add(projectNode);
                    selectedNode ??= matched;
                }

                // Top-level groups for this category
                var topLevelGroups = groups
                    .Where(g => string.IsNullOrEmpty(g.ParentGroup))
                    .OrderBy(g => g.SortOrder)
                    .ToList();

                foreach (var group in topLevelGroups)
                {
                    var (groupNode, groupMatched) = BuildGroupNodeData(group, projects, ref selectedNode);
                    categoryChildren.Add(groupNode);
                    selectedNode ??= groupMatched;
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
                    BadgeText = projects.Count.ToString(),
                    Tag = "Header",
                    IconSymbol = categoryIcon,
                    FontSize = 15,
                    FontWeight = FontWeight.Bold,
                    IsExpanded = true,
                    NodeOpacity = 0.9,
                    NodeMargin = nodes.Count > 0 ? new Avalonia.Thickness(0, 6, 0, 0) : new Avalonia.Thickness(0),
                    NodeMinHeight = 24,
                    Children = categoryChildren
                });
            }

            TreeNodes = nodes;
            SelectedTreeNode = selectedNode;
        }

        /// <summary>
        /// Builds a group node, including any subgroups and their projects.
        /// </summary>
        private (TreeNodeData node, TreeNodeData? matched) BuildGroupNodeData(
            Finn.Model.GroupData group,
            List<Finn.Model.ProjectData> categoryProjects,
            ref TreeNodeData? selectedNode)
        {
            var groupChildren = new List<TreeNodeData>();

            // Direct projects in this group
            foreach (var project in categoryProjects.Where(p => p.Parent == group.Name))
            {
                var (projectNode, matched) = BuildProjectNodeData(project);
                groupChildren.Add(projectNode);
                selectedNode ??= matched;
            }

            // Subgroups within this group
            var subGroups = Storage.ProjectGroups
                .Where(g => g.ParentGroup == group.Name)
                .OrderBy(g => g.SortOrder)
                .ToList();

            foreach (var sub in subGroups)
            {
                var subChildren = new List<TreeNodeData>();
                foreach (var project in categoryProjects.Where(p => p.Parent == sub.Name))
                {
                    var (projectNode, matched) = BuildProjectNodeData(project);
                    subChildren.Add(projectNode);
                    selectedNode ??= matched;
                }

                groupChildren.Add(new TreeNodeData
                {
                    Header = sub.Name,
                    Tag = "Subgroup",
                    GroupName = sub.Name,
                    IconSymbol = "FolderOpen",
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    IsExpanded = true,
                    NodeOpacity = 0.88,
                    BadgeText = subChildren.Count > 0 ? subChildren.Count.ToString() : null,
                    Children = subChildren
                });
            }

            var groupNode = new TreeNodeData
            {
                Header = group.Name,
                Tag = "Group",
                GroupName = group.Name,
                IconSymbol = "FolderMultiple",
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                IsExpanded = true,
                NodeOpacity = 0.92,
                BadgeText = groupChildren.Count(c => c.Tag == "All Types") > 0 
                                ? groupChildren.Count(c => c.Tag == "All Types").ToString() 
                                : null,
                NodeMargin = new Avalonia.Thickness(0, 4, 0, 0),
                Children = groupChildren
            };

            return (groupNode, null);
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
                    if (child.Tag is "Group" or "Subgroup")
                    {
                        foreach (var groupChild in child.Children)
                        {
                            if (groupChild.Tag is "Subgroup")
                            {
                                foreach (var projectNode in groupChild.Children)
                                    selectedNode ??= TrySelectProjectNode(projectNode);
                            }
                            else
                                selectedNode ??= TrySelectProjectNode(groupChild);
                        }
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
            if (projectNode.Tag != "All Types" || projectNode.Header != CurrentProject?.Namn)
            {
                projectNode.IsExpanded = false;
                return null;
            }

            projectNode.IsExpanded = true;

            // Only drill into a filetype child when a specific type is active.
            // When viewing "All Types", stay on the project node.
            string? targetType = Type is not null and not "All Types" ? Type : null;

            if (targetType != null)
            {
                foreach (var child in projectNode.Children)
                {
                    string childFiletype = child.Header;
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
                    Header = filetype,
                    BadgeText = count.ToString(),
                    Tag = project.Namn,
                    IconSymbol = GetFiletypeIcon(filetype),
                    FontSize = 13,
                    FontWeight = FontWeight.Light,
                    Foreground = foreground,
                    NodeOpacity = 0.75
                };

                // Match the filetype node when a file is selected in the current project.
                // Skip when Type is "All Types" so the project-level node stays selected.
                if (isCurrent && Type != null && Type != ALL_TYPES && filetype == Type)
                    matched = child;
                else if (isCurrent && Type is null or "All Types" && matched == null)
                { /* viewing all files — don't drill into a filetype child */ }
                else if (isCurrent && matched == null && CurrentFile?.Filtyp == filetype)
                    matched = child;

                children.Add(child);
            }

            var node = new TreeNodeData
            {
                Header = project.Namn,
                Tag = "All Types",
                IconSymbol = project.IsShared ? "People" : "Folder",
                SyncIconSymbol = project.IsShared ? project.SharedSyncIconSymbol : null,
                SyncTooltip = project.IsShared ? project.SharedSyncStatus.ToString() : null,
                IsViewer = project.IsViewer,
                ViewerTooltip = project.IsViewer ? "Read-only viewer" : null,
                FontSize = 14,
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