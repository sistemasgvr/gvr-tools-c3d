using System.Windows;
using GvrTools.UI.Icons;
using GvrTools.UI.Services;

namespace GvrTools.Licensing.Activation
{
    public partial class AccountLicenseWindow : Window
    {
        private readonly AccountLicenseViewModel _viewModel;
        private bool _activated;

        public AccountLicenseWindow(AccountLicenseViewModel viewModel)
        {
            InitializeComponent();
            WpfHostGuard.EnsureExplicitShutdown();
            _viewModel = viewModel;
            DataContext = viewModel;
            viewModel.RequestClose += OnRequestClose;

            Icon = BrandIcons.Escudo;
            HeaderIcon.Source = BrandIcons.Escudo;
        }

        /// <summary>true si el plan cambió (activó o desactivó) en esta sesión de diálogo -- el host debe reiniciar Revit.</summary>
        public bool NeedsRestart => _activated;

        /// <summary>Texto a mostrar en el aviso de reinicio -- distinto según activó o desactivó.</summary>
        public string RestartReason => _viewModel.RestartReason;

        private void OnRequestClose(bool activated)
        {
            _activated = activated;
            DialogResult = activated;
            Close();
        }
    }
}
