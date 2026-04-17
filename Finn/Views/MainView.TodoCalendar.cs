using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using System;
using Avalonia.Interactivity;
using System.Linq;
using Finn.ViewModels;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Input.Platform;
using System.Collections.Generic;
using Finn.Model;
using System.IO;
using Avalonia.Platform.Storage;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Finn.Views;

public partial class MainView
{
#region Todo

    private void OnTodoContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        var grid = this.FindControl<DataGrid>("TodoGrid");

        // Find the "Add Subtask" menu item by name
        MenuItem? subtaskItem = null;
        foreach (var child in menu.Items)
        {
            if (child is MenuItem mi && mi.Name == "AddSubtaskMenuItem")
            {
                subtaskItem = mi;
                break;
            }
        }
        if (subtaskItem == null) return;

        // Hide "Add Subtask" when no item is selected or the selected item is already a subtask
        subtaskItem.IsVisible = grid?.SelectedItem is Model.TodoItem item && item.IndentLevel == 0;
    }

    private void OnAddTodo(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var items = _ctx.CurrentProject?.TodoItems;
        if (items == null) return;
        var task = new Model.TodoItem { Text = "New task" };
        items.Add(task);
        _ctx.MarkDirty();

        var grid = this.FindControl<DataGrid>("TodoGrid");
        if (grid != null)
        {
            grid.SelectedItem = task;
            Dispatcher.UIThread.Post(() => grid.BeginEdit(), DispatcherPriority.Input);
        }
    }

    private void OnAddSubtask(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var grid = this.FindControl<DataGrid>("TodoGrid");
        var items = _ctx.CurrentProject?.TodoItems;
        if (grid == null || items == null) return;

        if (grid.SelectedItem is not Model.TodoItem parent) return;
        // Only allow subtasks on top-level items
        if (parent.IndentLevel > 0) return;

        int parentIndex = items.IndexOf(parent);
        if (parentIndex < 0) return;

        // Find the insertion point: after the parent and all its existing children
        int insertAt = parentIndex + 1;
        while (insertAt < items.Count && items[insertAt].IndentLevel > parent.IndentLevel)
            insertAt++;

        var subtask = new Model.TodoItem
        {
            Text = "New subtask",
            IndentLevel = 1
        };
        items.Insert(insertAt, subtask);
        _ctx.MarkDirty();

        grid.SelectedItem = subtask;
        Dispatcher.UIThread.Post(() => grid.BeginEdit(), DispatcherPriority.Input);
    }

    private void OnRemoveTodo(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var grid = this.FindControl<DataGrid>("TodoGrid");
        var items = _ctx.CurrentProject?.TodoItems;
        if (grid == null || items == null) return;
        if (grid.SelectedItem is not Model.TodoItem item) return;

        int index = items.IndexOf(item);
        if (index < 0) return;

        // Remove the item and any children nested beneath it
        int removeCount = 1;
        while (index + removeCount < items.Count && items[index + removeCount].IndentLevel > item.IndentLevel)
            removeCount++;

        for (int i = 0; i < removeCount; i++)
            items.RemoveAt(index);
        _ctx.MarkDirty();
    }

    private void OnTodoColor(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var grid = this.FindControl<DataGrid>("TodoGrid");
        if (grid?.SelectedItem is not Model.TodoItem item) return;
        string? color = sender switch
        {
            MenuItem { Tag: string c } => c,
            Button { Tag: string c } => c,
            _ => null
        };
        if (color != null)
        {
            item.Color = color;
            _ctx.MarkDirty();
        }
    }

    // ── Todo list helpers ──────────────────────────────────────────────

    /// <summary>
    /// Returns the number of contiguous children that follow <paramref name="index"/>
    /// (items whose IndentLevel is greater than items[index].IndentLevel).
    /// </summary>
    private static int CountChildren(System.Collections.ObjectModel.ObservableCollection<Model.TodoItem> items, int index)
    {
        int level = items[index].IndentLevel;
        int count = 0;
        while (index + 1 + count < items.Count && items[index + 1 + count].IndentLevel > level)
            count++;
        return count;
    }

    /// <summary>
    /// For a subtask, returns the flat-list index range [childrenStart, childrenEnd)
    /// of the parent's children block. The subtask can only move within this range.
    /// For a top-level item the range is the whole list, restricted to top-level peers.
    /// </summary>
    private static (int Start, int End) GetParentChildrenRange(
        System.Collections.ObjectModel.ObservableCollection<Model.TodoItem> items, int itemIndex)
    {
        var item = items[itemIndex];
        if (item.IndentLevel == 0)
            return (0, items.Count);

        // Walk backwards to the parent (first item with lower indent level)
        int parentIndex = itemIndex - 1;
        while (parentIndex >= 0 && items[parentIndex].IndentLevel >= item.IndentLevel)
            parentIndex--;
        if (parentIndex < 0) return (0, items.Count); // shouldn't happen, defensive

        // The children block starts right after the parent and ends where indent drops
        int start = parentIndex + 1;
        int end = start;
        while (end < items.Count && items[end].IndentLevel > items[parentIndex].IndentLevel)
            end++;
        return (start, end);
    }

    /// <summary>
    /// Finds the previous sibling of the item at <paramref name="index"/> within the
    /// given range, at the same indent level. Returns -1 if none exists.
    /// A "sibling" is the nearest item at the same indent level scanning backwards,
    /// skipping over any children blocks that belong to other siblings.
    /// </summary>
    private static int FindPrevSibling(
        System.Collections.ObjectModel.ObservableCollection<Model.TodoItem> items, int index, int rangeStart)
    {
        int myLevel = items[index].IndentLevel;
        int i = index - 1;
        while (i >= rangeStart)
        {
            if (items[i].IndentLevel == myLevel) return i;
            if (items[i].IndentLevel < myLevel) return -1; // crossed parent boundary
            i--;
        }
        return -1;
    }

    /// <summary>
    /// Finds the next sibling of the block starting at <paramref name="index"/>
    /// (with <paramref name="blockSize"/> items) within the given range.
    /// Returns -1 if none exists.
    /// </summary>
    private static int FindNextSibling(
        System.Collections.ObjectModel.ObservableCollection<Model.TodoItem> items, int index, int blockSize, int rangeEnd)
    {
        int nextIndex = index + blockSize;
        if (nextIndex >= rangeEnd) return -1;
        // Verify it's at the same level
        if (items[nextIndex].IndentLevel != items[index].IndentLevel) return -1;
        return nextIndex;
    }

    // ── Move ──────────────────────────────────────────────────────────

    private void MoveTodoItem(int fromIndex, int direction)
    {
        var grid = this.FindControl<DataGrid>("TodoGrid");
        var items = _ctx.CurrentProject?.TodoItems;
        if (grid == null || items == null) return;
        grid.CancelEdit();
        if (fromIndex < 0 || fromIndex >= items.Count) return;

        int myChildren = CountChildren(items, fromIndex);
        int myBlock = 1 + myChildren;
        var (rangeStart, rangeEnd) = GetParentChildrenRange(items, fromIndex);

        if (direction < 0) // ── Move up ──
        {
            int prevSib = FindPrevSibling(items, fromIndex, rangeStart);
            if (prevSib < 0) return; // already first among siblings

            int prevChildren = CountChildren(items, prevSib);
            int prevBlock = 1 + prevChildren;

            // Extract our block, remove it, re-insert it before the previous sibling.
            var myItems = new Model.TodoItem[myBlock];
            for (int i = 0; i < myBlock; i++)
                myItems[i] = items[fromIndex + i];
            for (int i = myBlock - 1; i >= 0; i--)
                items.RemoveAt(fromIndex + i);
            for (int i = 0; i < myBlock; i++)
                items.Insert(prevSib + i, myItems[i]);

            grid.SelectedIndex = prevSib;
        }
        else // ── Move down ──
        {
            int nextSib = FindNextSibling(items, fromIndex, myBlock, rangeEnd);
            if (nextSib < 0) return; // already last among siblings

            int nextChildren = CountChildren(items, nextSib);
            int nextBlock = 1 + nextChildren;

            // Extract the next sibling's block, remove it, re-insert it before our block.
            var nextItems = new Model.TodoItem[nextBlock];
            for (int i = 0; i < nextBlock; i++)
                nextItems[i] = items[nextSib + i];
            for (int i = nextBlock - 1; i >= 0; i--)
                items.RemoveAt(nextSib + i);
            for (int i = 0; i < nextBlock; i++)
                items.Insert(fromIndex + i, nextItems[i]);

            grid.SelectedIndex = fromIndex + nextBlock;
        }
        _ctx.MarkDirty();
    }

    private void OnMoveTodoUp(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var grid = this.FindControl<DataGrid>("TodoGrid");
        if (grid == null) return;
        int index = grid.SelectedIndex;
        if (index > 0)
            MoveTodoItem(index, -1);
    }

    private void OnMoveTodoDown(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var grid = this.FindControl<DataGrid>("TodoGrid");
        var items = _ctx.CurrentProject?.TodoItems;
        if (grid == null || items == null) return;
        int index = grid.SelectedIndex;
        if (index >= 0 && index < items.Count - 1)
            MoveTodoItem(index, 1);
    }

    private void OnClearCompletedTodos(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var items = _ctx.CurrentProject?.TodoItems;
        if (items == null) return;
        bool any = false;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i].IsDone)
            {
                int indent = items[i].IndentLevel;
                int end = i + 1;
                while (end < items.Count && items[end].IndentLevel > indent)
                    end++;
                for (int j = end - 1; j >= i; j--)
                    items.RemoveAt(j);
                any = true;
            }
        }
        if (any) _ctx.MarkDirty();
    }

    private System.Collections.ObjectModel.ObservableCollection<Model.TodoItem>? _subscribedTodoItems;

    internal void SubscribeTodoItems()
    {
        // Unsubscribe from previous collection
        if (_subscribedTodoItems != null)
        {
            _subscribedTodoItems.CollectionChanged -= TodoItems_CollectionChanged;
            foreach (var item in _subscribedTodoItems)
                item.PropertyChanged -= TodoItem_PropertyChanged;
        }

        _subscribedTodoItems = _ctx.CurrentProject?.TodoItems;
        if (_subscribedTodoItems == null) return;

        _subscribedTodoItems.CollectionChanged += TodoItems_CollectionChanged;
        foreach (var item in _subscribedTodoItems)
            item.PropertyChanged += TodoItem_PropertyChanged;

        UpdateTodoEmptyHint();
    }

    private void TodoItems_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (Model.TodoItem item in e.OldItems)
                item.PropertyChanged -= TodoItem_PropertyChanged;
        if (e.NewItems != null)
            foreach (Model.TodoItem item in e.NewItems)
                item.PropertyChanged += TodoItem_PropertyChanged;
        UpdateTodoEmptyHint();
    }

    private void TodoItem_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _ctx.MarkDirty();
    }

    private void UpdateTodoEmptyHint()
    {
        var items = _ctx.CurrentProject?.TodoItems;
        TodoEmptyHint.IsVisible = items == null || items.Count == 0;
    }

    #endregion

    #region Collections

    private void OnNewCollection(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(CollectionInput.Text))
        {
            _ctx.Collections.NewCollection(CollectionInput.Text);
            CollectionInput.Clear();
            UpdateCollectionsEmptyHint();
        }
    }

    private void OnRenameCollection(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(CollectionInput.Text))
        {
            _ctx.Collections.RenameCollection(CollectionInput.Text);
            CollectionInput.Clear();
        }
    }

    private void OnAddToCollection(object? sender, RoutedEventArgs e)
    {
        string? name = sender switch
        {
            MenuItem { SelectedItem: string s } => s,
            Button { Content: string c } => c,
            _ => null
        };
        if (name != null)
            _ctx.Collections.AddFileToCollection(name);

        if (sender is Button)
            CollectionButton.Flyout?.Hide();

        UpdateCollectionsEmptyHint();
    }

    #endregion

#region Calendar

    private async void OnCopyDiaryEntry(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Model.WeekDiaryEntry entry
            && !string.IsNullOrWhiteSpace(entry.Diary))
        {
            var top = (TopLevel)ParentWindow;
            if (top?.Clipboard != null)
                await top.Clipboard.SetTextAsync(entry.Diary);
        }
    }

    /// <summary>
    /// Called when the Calendar control navigates to a different month.
    /// </summary>
    private void OnCalendarMonthChanged(object? sender, CalendarDateChangedEventArgs e)
    {
        // Delay so the CalendarDayButtons have been laid out for the new month
        Dispatcher.UIThread.Post(RefreshCalendarDayIndicators, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Walks the visual tree of the calendar control and adds small coloured
    /// dot indicators to each <see cref="CalendarDayButton"/> that has timesheet
    /// entries, notes, or reminders.
    /// </summary>
    private void RefreshCalendarDayIndicators()
    {
        var calendar = this.FindControl<Calendar>("MainCalendar");
        if (calendar is null || _ctx?.Calendar is null) return;

        var displayDate = calendar.DisplayDate;
        var dayButtons = calendar.GetVisualDescendants().OfType<CalendarDayButton>();

        foreach (var btn in dayButtons)
        {
            // Resolve the template root Panel so we can add/remove our indicator
            var rootPanel = btn.GetVisualChildren().FirstOrDefault() as Panel;
            if (rootPanel is null) continue;

            // Remove any previously-added indicator panel
            for (int i = rootPanel.Children.Count - 1; i >= 0; i--)
            {
                if (rootPanel.Children[i] is Avalonia.Controls.StackPanel sp && sp.Name == "_DayInd")
                    rootPanel.Children.RemoveAt(i);
            }

            // Skip inactive (previous/next month) day buttons
            if (btn.Classes.Contains(":inactive")) continue;

            // Parse the day number from the button's Content
            if (btn.Content is not string dayStr || !int.TryParse(dayStr, out int day)) continue;
            if (day < 1 || day > DateTime.DaysInMonth(displayDate.Year, displayDate.Month)) continue;

            var date = new DateOnly(displayDate.Year, displayDate.Month, day);
            var (hasNote, hasTime, hasReminder) = _ctx.Calendar.GetDayInfo(date);
            if (!hasNote && !hasTime && !hasReminder) continue;

            var indicator = new Avalonia.Controls.StackPanel
            {
                Name = "_DayInd",
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                Spacing = 2,
                Margin = new Thickness(0, 0, 0, 4),
                IsHitTestVisible = false,
            };

            if (hasTime)
                indicator.Children.Add(new Avalonia.Controls.Shapes.Ellipse
                    { Width = 5, Height = 5, Fill = Brushes.DodgerBlue });
            if (hasNote)
                indicator.Children.Add(new Avalonia.Controls.Shapes.Ellipse
                    { Width = 5, Height = 5, Fill = Brushes.MediumSeaGreen });
            if (hasReminder)
                indicator.Children.Add(new Avalonia.Controls.Shapes.Ellipse
                    { Width = 5, Height = 5, Fill = Brushes.Orange });

            rootPanel.Children.Add(indicator);
        }
    }

    #endregion
}
