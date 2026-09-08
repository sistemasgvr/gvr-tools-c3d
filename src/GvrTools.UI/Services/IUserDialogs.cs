namespace GvrTools.UI.Services
{
    /// <summary>
    /// The few OS-level interactions a tool window needs. Behind an interface so view models stay
    /// unit-testable and so there is exactly one place that is allowed to pop a window.
    /// </summary>
    public interface IUserDialogs
    {
        /// <summary>Shows a folder picker. Returns null when the user cancels.</summary>
        string PickFolder(string description, string initialPath);

        void ShowInfo(string title, string message);

        void ShowWarning(string title, string message);

        void ShowError(string title, string message);

        bool Confirm(string title, string message);

        /// <summary>Opens a folder (or selects a file) in File Explorer. Never throws.</summary>
        void Reveal(string path);

        /// <summary>Asks for one line of text. Returns null when the user cancels or closes the window.</summary>
        string PromptText(string title, string message, string defaultValue = "");
    }
}
