using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Finn.Utils
{
    /// <summary>
    /// An <see cref="ObservableCollection{T}"/> subclass that supports replacing
    /// all items in a single operation, firing only one <see cref="NotifyCollectionChangedAction.Reset"/>
    /// notification instead of per-item Add/Remove events.
    /// </summary>
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        /// <summary>
        /// Replaces every item in the collection with <paramref name="items"/>,
        /// raising a single <see cref="NotifyCollectionChangedAction.Reset"/> event.
        /// </summary>
        public void ReplaceAll(IEnumerable<T> items)
        {
            // Always snapshot — the source may be a lazy query over this same collection
            var snapshot = new List<T>(items);

            Items.Clear();

            foreach (var item in snapshot)
                Items.Add(item);

            RaiseReset();
        }

        /// <summary>
        /// Adds multiple items without per-item notifications,
        /// raising a single <see cref="NotifyCollectionChangedAction.Reset"/> event.
        /// </summary>
        public void AddRange(IEnumerable<T> items)
        {
            foreach (var item in items)
                Items.Add(item);

            RaiseReset();
        }

        /// <summary>
        /// Inserts multiple items starting at <paramref name="index"/> without
        /// per-item notifications, raising a single Reset event.
        /// </summary>
        public void InsertRange(int index, IEnumerable<T> items)
        {
            int i = index;
            foreach (var item in items)
                Items.Insert(i++, item);

            RaiseReset();
        }

        /// <summary>
        /// Removes <paramref name="count"/> items starting at <paramref name="index"/>
        /// without per-item notifications, raising a single Reset event.
        /// </summary>
        public void RemoveRange(int index, int count)
        {
            if (count <= 0) return;
            for (int i = 0; i < count; i++)
                Items.RemoveAt(index);

            RaiseReset();
        }

        /// <summary>
        /// Removes all items that match <paramref name="predicate"/> without
        /// per-item notifications, raising a single Reset event.
        /// </summary>
        public void RemoveAll(Func<T, bool> predicate)
        {
            var kept = new List<T>();
            bool anyRemoved = false;
            foreach (var item in Items)
            {
                if (predicate(item))
                    anyRemoved = true;
                else
                    kept.Add(item);
            }

            if (anyRemoved)
            {
                Items.Clear();
                foreach (var item in kept)
                    Items.Add(item);
                RaiseReset();
            }
        }

        private void RaiseReset()
        {
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
