using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Finn.ViewModels
{
    /// <summary>
    /// Base class for all view models, provides property change notification
    /// and async disposal support.
    /// </summary>
    public abstract class ViewModelBase : ObservableObject, IAsyncDisposable
    {
        protected readonly ILogger? logger;

        protected ViewModelBase(ILogger? logger = null)
        {
            this.logger = logger;
        }

        protected new bool SetProperty<T>(ref T field, T newValue, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, newValue))
                return false;

            OnPropertyChanging(propertyName);
            field = newValue;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected bool SetProperty<T>(ref T field, T newValue, Action onChanged, [CallerMemberName] string? propertyName = null)
        {
            if (SetProperty(ref field, newValue, propertyName))
            {
                onChanged?.Invoke();
                return true;
            }
            return false;
        }

        protected virtual ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        public override string ToString() => GetType().FullName ?? base.ToString();
    }
}
