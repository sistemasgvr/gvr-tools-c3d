namespace GvrTools.Core.Settings
{
    /// <summary>
    /// Per-user persistence for a tool's preferences. Each tool owns one settings class and one
    /// key, so tools never step on each other's stored state.
    /// </summary>
    public interface ISettingsStore
    {
        /// <summary>
        /// Returns the stored settings for <paramref name="key"/>, or a default-constructed
        /// instance when nothing is stored yet or the stored data is unreadable. Never throws.
        /// </summary>
        T Load<T>(string key) where T : class, new();

        /// <summary>Persists <paramref name="value"/> under <paramref name="key"/>. Never throws.</summary>
        void Save<T>(string key, T value) where T : class;
    }
}
