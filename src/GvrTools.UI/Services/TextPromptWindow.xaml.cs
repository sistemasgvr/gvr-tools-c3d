using System.Windows;
using System.Windows.Input;

namespace GvrTools.UI.Services
{
    /// <summary>
    /// Minimal modal "type one line of text" dialog. Code-behind only on purpose: it has no state
    /// worth a view model, and it is only ever driven through <see cref="WindowsUserDialogs.PromptText"/>.
    /// </summary>
    public partial class TextPromptWindow : Window
    {
        public TextPromptWindow()
        {
            InitializeComponent();
            WpfHostGuard.EnsureExplicitShutdown();
            Loaded += OnLoaded;
        }

        /// <summary>The typed value once the window closes with <see cref="Window.DialogResult"/> true.</summary>
        public string Value { get; private set; }

        // "Aceptar" tiene IsDefault="True" en el XAML, que ya hace que Enter lo dispare -- pero el
        // TextBox también tiene su propio KeyDown para Enter (más confiable con foco dentro de un
        // TextBox en algunos escenarios de enrutamiento). Sin esta guarda, un solo Enter podía
        // invocar Accept() dos veces (una vía KeyDown, otra vía el botón default) y la segunda
        // llamada intentaba cerrar una ventana ya cerrada, lo que WPF lanza como excepción.
        private bool _closed;

        public void Configure(string title, string message, string defaultValue)
        {
            Title = string.IsNullOrWhiteSpace(title) ? "GVR Tools" : title;
            MessageText.Text = message ?? string.Empty;
            ValueBox.Text = defaultValue ?? string.Empty;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => Accept();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_closed) return;
            _closed = true;
            DialogResult = false;
            Close();
        }

        private void ValueBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            Accept();
            // Evita que el mismo Enter se enrute de nuevo hacia el botón IsDefault="True".
            e.Handled = true;
        }

        private void Accept()
        {
            if (_closed) return;
            _closed = true;
            Value = ValueBox.Text;
            DialogResult = true;
            Close();
        }
    }
}
