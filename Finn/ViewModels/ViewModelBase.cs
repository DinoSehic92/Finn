using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Finn.ViewModels
{
    public abstract class ViewModelBase : ObservableObject, IAsyncDisposable
    {
        #region Fields
        private readonly SemaphoreSlim operationSemaphore = new(1, 1);
        private bool disposed = false;
        protected readonly ILogger? logger;
        #endregion

        #region Constructor
        protected ViewModelBase(ILogger? logger = null)
        {
            this.logger = logger;
        }
        #endregion

        #region Property Changed Enhancements
        protected bool SetProperty<T>(ref T field, T newValue, [CallerMemberName] string? propertyName = null)
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
        #endregion

        #region Async Operations
        protected async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            if (disposed) throw new ObjectDisposedException(GetType().Name);

            await operationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await operation().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error executing async operation in {ViewModelType}", GetType().Name);
                throw;
            }
            finally
            {
                operationSemaphore.Release();
            }
        }

        protected async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            if (disposed) throw new ObjectDisposedException(GetType().Name);

            await operationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error executing async operation in {ViewModelType}", GetType().Name);
                throw;
            }
            finally
            {
                operationSemaphore.Release();
            }
        }
        #endregion

        #region Disposal
        protected virtual async ValueTask DisposeAsyncCore()
        {
            if (!disposed)
            {
                operationSemaphore.Dispose();
                disposed = true;
            }
        }

        // Ensure exceptions from async dispose are logged to the file fallback
        public override string ToString()
        {
            return GetType().FullName ?? base.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region Validation
        protected bool ValidateProperty<T>(T value, [CallerMemberName] string? propertyName = null)
        {
            if (string.IsNullOrEmpty(propertyName))
                return true;

            // Add validation logic here if needed
            return true;
        }
        #endregion
    }
}
