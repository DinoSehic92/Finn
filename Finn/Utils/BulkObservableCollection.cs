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
            Items.Clear();

            foreach (var item in items)
                Items.Add(item);

            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
