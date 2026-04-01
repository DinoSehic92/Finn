using Avalonia.Media;
using System;
using System.ComponentModel;

namespace Finn.Model
{
    /// <summary>
    /// A single to-do item stored per project.
    /// </summary>
    public class TodoItem : INotifyPropertyChanged
    {
        private string text = string.Empty;
        public string Text
        {
            get => text;
            set { text = value; RaisePropertyChanged(nameof(Text)); }
        }

        private bool isDone;
        public bool IsDone
        {
            get => isDone;
            set { isDone = value; RaisePropertyChanged(nameof(IsDone)); }
        }

        private string color = string.Empty;
        /// <summary>
        /// Optional color tag for the item (e.g. "#FF0000").
        /// Empty string means no color.
        /// </summary>
        public string Color
        {
            get => color;
            set { color = value ?? string.Empty; RaisePropertyChanged(nameof(Color)); }
        }

        private DateTime createdDate = DateTime.Now;
        public DateTime CreatedDate
        {
            get => createdDate;
            set { createdDate = value; RaisePropertyChanged(nameof(CreatedDate)); }
        }

        private void RaisePropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
