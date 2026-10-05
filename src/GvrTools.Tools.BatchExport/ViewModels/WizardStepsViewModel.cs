using System;
using System.Collections.Generic;
using GvrTools.UI.Mvvm;

namespace GvrTools.Tools.BatchExport.ViewModels
{
    /// <summary>
    /// Step-by-step flow over the window's tabs (1. what to plot → 2. how → 3. where).
    ///
    /// A tab opens only when every earlier step is complete and the user has already reached the
    /// one right before it, so nobody plots without having looked at the plot options and the
    /// output; once every step has been visited the user can jump freely between tabs. The host
    /// asks <see cref="HasReachedLastStep"/> before enabling "Trazar" and calls <see cref="Refresh"/>
    /// whenever a step's completeness may have changed.
    /// </summary>
    public sealed class WizardStepsViewModel : ObservableObject
    {
        private readonly Func<int, bool> _isStepComplete;
        private readonly IReadOnlyList<string> _hints;
        private int _selectedIndex;
        private int _maxVisited;

        /// <param name="isStepComplete">Whether step <c>i</c> has what the next step needs.</param>
        /// <param name="hints">One instruction per step, shown next to the navigation buttons.</param>
        public WizardStepsViewModel(Func<int, bool> isStepComplete, IReadOnlyList<string> hints)
        {
            _isStepComplete = isStepComplete ?? throw new ArgumentNullException(nameof(isStepComplete));
            _hints = hints ?? throw new ArgumentNullException(nameof(hints));

            NextCommand = new RelayCommand(() => SelectedIndex = _selectedIndex + 1, () => CanGoNext);
            BackCommand = new RelayCommand(() => SelectedIndex = _selectedIndex - 1, () => CanGoBack);
        }

        public int StepCount => _hints.Count;

        /// <summary>Bound to the TabControl; refuses tabs that are not reachable yet.</summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (value < 0 || value >= StepCount || !CanOpen(value))
                {
                    Raise(nameof(SelectedIndex));
                    return;
                }

                if (_selectedIndex == value) return;

                _selectedIndex = value;
                if (value > _maxVisited) _maxVisited = value;
                RaiseAll();
            }
        }

        /// <summary>The user went through every tab at least once.</summary>
        public bool HasReachedLastStep => _maxVisited >= StepCount - 1;

        public bool CanOpenStep1 => CanOpen(1);

        public bool CanOpenStep2 => CanOpen(2);

        public bool CanGoNext => _selectedIndex < StepCount - 1 && CanOpen(_selectedIndex + 1);

        public bool CanGoBack => _selectedIndex > 0;

        public string StepHint => $"Paso {_selectedIndex + 1} de {StepCount}: {_hints[_selectedIndex]}";

        public RelayCommand NextCommand { get; }

        public RelayCommand BackCommand { get; }

        public bool CanOpen(int step)
        {
            if (step <= 0) return true;
            if (step > _maxVisited + 1) return false;

            for (int i = 0; i < step; i++)
            {
                if (!_isStepComplete(i)) return false;
            }

            return true;
        }

        /// <summary>
        /// Re-evaluates which tabs are reachable; if the current one no longer is (e.g. every
        /// layout was unticked), goes back to the first step that needs attention.
        /// </summary>
        public void Refresh()
        {
            int index = _selectedIndex;
            while (index > 0 && !CanOpen(index)) index--;
            _selectedIndex = index;
            RaiseAll();
        }

        private void RaiseAll()
        {
            Raise(nameof(SelectedIndex), nameof(HasReachedLastStep), nameof(CanOpenStep1), nameof(CanOpenStep2),
                nameof(CanGoNext), nameof(CanGoBack), nameof(StepHint));
            NextCommand?.RaiseCanExecuteChanged();
            BackCommand?.RaiseCanExecuteChanged();
        }
    }
}
