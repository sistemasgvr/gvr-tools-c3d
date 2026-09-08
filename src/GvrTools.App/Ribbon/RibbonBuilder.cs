using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.Windows;
using GvrTools.Civil3D.Ribbon;
using GvrTools.Core.Diagnostics;

namespace GvrTools.App.Ribbon
{
    /// <summary>
    /// Turns a list of tools into ribbon buttons on AutoCAD's own ribbon (<c>Autodesk.Windows</c>
    /// — the CUI-backed control shared by AutoCAD, Civil 3D and every AutoCAD-based vertical),
    /// instead of Revit's <c>UIControlledApplication.CreateRibbonTab</c>/<c>RibbonPanel</c>.
    ///
    /// Creates the tab once, creates each panel on demand, and adds one button per tool that runs
    /// the tool's AutoCAD command via <c>RibbonButton.CommandParameter</c> (cancel bytes +
    /// <c>_COMMANDNAME </c> sent with <c>SendStringToExecute</c>).
    /// </summary>
    public sealed class RibbonBuilder
    {
        private readonly string _tabName;
        private readonly ILog _log;
        private readonly Dictionary<string, RibbonPanel> _panels = new Dictionary<string, RibbonPanel>(StringComparer.OrdinalIgnoreCase);
        private readonly RibbonTab _tab;

        public RibbonBuilder(string tabName, ILog log)
        {
            _tabName = tabName;
            _log = log;

            _tab = CreateTab();
        }

        /// <summary>Adds one button. Returns false when it could not be created, which is logged but not fatal.</summary>
        public bool Add(ICivilTool tool)
        {
            if (_tab == null) return false;

            try
            {
                RibbonPanel panel = ResolvePanel(tool.PanelName);

                var button = new RibbonButton
                {
                    Text = tool.Title,
                    ShowText = true,
                    ShowImage = true,
                    Orientation = System.Windows.Controls.Orientation.Vertical,
                    Size = RibbonItemSize.Large,
                    // Use real Ctrl+C bytes (\x03), not the two-character caret sequence "^C".
                    // SendStringToExecute treats "^C" as literal text in some hosts and then reports
                    // the whole string (e.g. "^C^C_GVRBATCHEXPORT") as an unknown command.
                    CommandParameter = "\x03\x03_" + tool.CommandName + " ",
                    CommandHandler = new RibbonCommandHandler(),
                    ToolTip = BuildTooltip(tool)
                };

                BitmapSource icon = SafeIcon(tool);
                if (icon != null)
                {
                    button.LargeImage = icon;
                    button.Image = icon;
                }

                panel.Source.Items.Add(button);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"No se pudo agregar el botón de la herramienta '{tool.Id}'.", ex);
                return false;
            }
        }

        private RibbonTab CreateTab()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null)
            {
                _log.Error("No se encontró el control de cinta de AutoCAD (ComponentManager.Ribbon es null).");
                return null;
            }

            foreach (RibbonTab existing in ribbon.Tabs)
            {
                if (string.Equals(existing.Title, _tabName, StringComparison.OrdinalIgnoreCase))
                    return existing;
            }

            var tab = new RibbonTab { Title = _tabName, Id = "GVR_TOOLS_TAB" };
            ribbon.Tabs.Add(tab);
            return tab;
        }

        private RibbonPanel ResolvePanel(string panelName)
        {
            string name = string.IsNullOrWhiteSpace(panelName) ? "General" : panelName;

            if (_panels.TryGetValue(name, out RibbonPanel cached)) return cached;

            foreach (RibbonPanel existing in _tab.Panels)
            {
                if (!string.Equals(existing.Source.Title, name, StringComparison.OrdinalIgnoreCase)) continue;

                _panels[name] = existing;
                return existing;
            }

            var source = new RibbonPanelSource { Title = name };
            var created = new RibbonPanel { Source = source };
            _tab.Panels.Add(created);
            _panels[name] = created;
            return created;
        }

        private static string BuildTooltip(ICivilTool tool)
        {
            if (string.IsNullOrWhiteSpace(tool.LongDescription)) return tool.Tooltip;
            return tool.Tooltip + Environment.NewLine + Environment.NewLine + tool.LongDescription;
        }

        private BitmapSource SafeIcon(ICivilTool tool)
        {
            try
            {
                return tool.CreateIcon() as BitmapSource;
            }
            catch (Exception ex)
            {
                _log.Warn($"El ícono de '{tool.Id}' no se pudo generar: {ex.Message}");
                return null;
            }
        }

        /// <summary>Sends the button's command string to AutoCAD's active document command queue.</summary>
        private sealed class RibbonCommandHandler : System.Windows.Input.ICommand
        {
#pragma warning disable CS0067 // required by ICommand, never raised: ribbon buttons are always enabled
            public event EventHandler CanExecuteChanged;
#pragma warning restore CS0067

            public bool CanExecute(object parameter) => true;

            public void Execute(object parameter)
            {
                var button = parameter as RibbonButton;
                if (button?.CommandParameter == null) return;

                var document = Application.DocumentManager.MdiActiveDocument;
                if (document == null) return;

                // echo=false avoids littering the command line with cancel bytes / the command name.
                document.SendStringToExecute((string)button.CommandParameter, true, false, false);
            }
        }
    }
}
