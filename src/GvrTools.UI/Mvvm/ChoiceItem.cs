using System.Collections.Generic;

namespace GvrTools.UI.Mvvm
{
    /// <summary>
    /// A value plus the label to show for it, for combo boxes over enums.
    ///
    /// Binding with <c>SelectedValuePath="Value"</c> and <c>DisplayMemberPath="Label"</c> keeps the
    /// user-facing wording in the view model, where it can be translated, instead of hiding it in a
    /// value converter.
    /// </summary>
    public sealed class ChoiceItem<T>
    {
        public ChoiceItem(T value, string label)
        {
            Value = value;
            Label = label;
        }

        public T Value { get; }

        public string Label { get; }

        public override string ToString() => Label;
    }

    public static class ChoiceItem
    {
        public static ChoiceItem<T> Of<T>(T value, string label) => new ChoiceItem<T>(value, label);

        public static IReadOnlyList<ChoiceItem<T>> List<T>(params ChoiceItem<T>[] items) => items;
    }
}
