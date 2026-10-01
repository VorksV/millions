using System;
using System.Windows;

namespace VoltrisOptimizer.UI.Behaviors
{
    /// <summary>
    /// BASE BEHAVIOR CLASS for WPF Behaviors
    /// Provides common functionality for attached behaviors
    /// Required for LicenseGuardBehavior inheritance
    /// </summary>
    public abstract class Behavior<T> : IDisposable where T : DependencyObject
    {
        private T? _associatedObject;
        private bool _isAttached = false;

        /// <summary>
        /// Gets the object to which this behavior is attached.
        /// </summary>
        public T AssociatedObject
        {
            get => _associatedObject!;
            private set
            {
                if (_associatedObject != null && !ReferenceEquals(_associatedObject, value))
                {
                    OnDetaching();
                }

                _associatedObject = value;

                if (_associatedObject != null && !_isAttached)
                {
                    OnAttached();
                    _isAttached = true;
                }
            }
        }

        /// <summary>
        /// Called after the behavior is attached to an AssociatedObject.
        /// </summary>
        protected abstract void OnAttached();

        /// <summary>
        /// Called when the behavior is being detached from its AssociatedObject,
        /// but before it has actually occurred.
        /// </summary>
        protected virtual void OnDetaching()
        {
        }

        /// <summary>
        /// Attaches the behavior to the specified object.
        /// </summary>
        /// <param name="dependencyObject">The object to which to attach the behavior.</param>
        public void Attach(T dependencyObject)
        {
            if (dependencyObject == null)
            {
                throw new ArgumentNullException(nameof(dependencyObject));
            }

            AssociatedObject = dependencyObject;
        }

        /// <summary>
        /// Detaches this behavior from the AssociatedObject.
        /// </summary>
        public void Detach()
        {
            OnDetaching();
            _isAttached = false;
            _associatedObject = default(T);
        }

        #region IDisposable

        /// <summary>
        /// Releases all resources used by the current instance of the Behavior class.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the Behavior and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Detach();
            }
        }

        #endregion
    }

    /// <summary>
    /// Generic Behavior base class for FrameworkElement
    /// </summary>
    public abstract class Behavior : Behavior<FrameworkElement>
    {
    }
}
