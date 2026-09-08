using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GvrTools.UI.Mvvm
{
    /// <summary>Base class for view models: property-change plumbing and nothing else.</summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Assigns <paramref name="field"/> and raises the change notification when the value
        /// actually changed. Returns whether anything changed, so callers can chain side effects.
        /// </summary>
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;

            field = value;
            Raise(propertyName);
            return true;
        }

        protected void Raise([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected void Raise(params string[] propertyNames)
        {
            if (propertyNames == null) return;

            foreach (string name in propertyNames)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
