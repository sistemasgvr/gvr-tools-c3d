using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace GvrTools.Installer
{
    /// <summary>
    /// Copies the adjacent <c>GvrTools.bundle</c> into ApplicationPlugins.
    /// Version selection is done by Civil 3D itself via PackageContents.xml SeriesMin/Max —
    /// this installer only detects installs so it can show a clear confirmation message.
    /// </summary>
    internal static class Program
    {
        private const string BundleName = "GvrTools.bundle";
        private static readonly int[] SupportedYears = { 2021, 2022, 2023, 2024, 2025, 2026, 2027 };

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool uninstall = args.Any(a =>
                string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "-uninstall", StringComparison.OrdinalIgnoreCase));

            try
            {
                if (uninstall) Uninstall();
                else Install();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error: " + ex.Message,
                    "GVR Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void Install()
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string sourceBundle = Path.Combine(exeDir, BundleName);

            if (!Directory.Exists(sourceBundle) ||
                !File.Exists(Path.Combine(sourceBundle, "PackageContents.xml")))
            {
                MessageBox.Show(
                    "No se encontró la carpeta \"" + BundleName + "\" junto a este instalador.\n" +
                    "Descomprime el ZIP completo y vuelve a ejecutar Instalar-GvrTools.exe.",
                    "GVR Tools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            IReadOnlyList<int> detected = DetectCivil3DYears();
            IReadOnlyList<int> packaged = GetPackagedYears(sourceBundle);

            var message = new StringBuilder();
            message.AppendLine("Se instalará GVR Tools para Civil 3D.");
            message.AppendLine();
            message.AppendLine("Versiones incluidas en el paquete:");
            message.AppendLine("  " + (packaged.Count == 0 ? "(ninguna)" : string.Join(", ", packaged)));
            message.AppendLine();

            if (detected.Count == 0)
            {
                message.AppendLine("No se detectó Civil 3D en este equipo.");
                message.AppendLine("El complemento se instalará igual; funcionará cuando abras una versión compatible.");
            }
            else
            {
                message.AppendLine("Civil 3D detectado:");
                message.AppendLine("  " + string.Join(", ", detected));

                var matched = detected.Where(y => packaged.Contains(y)).ToList();
                var missing = detected.Where(y => !packaged.Contains(y)).ToList();

                if (matched.Count > 0)
                    message.AppendLine().AppendLine("Listo para: " + string.Join(", ", matched));
                if (missing.Count > 0)
                    message.AppendLine().AppendLine("Sin paquete para: " + string.Join(", ", missing));
            }

            message.AppendLine();
            message.AppendLine("Destino:");
            message.AppendLine("  " + GetTargetBundlePath());
            message.AppendLine();
            message.Append("¿Continuar?");

            if (MessageBox.Show(message.ToString(), "GVR Tools — Instalar",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            string target = GetTargetBundlePath();
            if (Directory.Exists(target))
                Directory.Delete(target, true);

            CopyDirectory(sourceBundle, target);

            MessageBox.Show(
                "Instalación completa.\n\nReinicia Civil 3D y busca la pestaña \"GVR Tools\".\n" +
                "Comando de prueba: GVRBATCHEXPORT",
                "GVR Tools",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static void Uninstall()
        {
            string target = GetTargetBundlePath();
            if (!Directory.Exists(target))
            {
                MessageBox.Show("GVR Tools no está instalado.", "GVR Tools",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(
                    "¿Quitar GVR Tools de?\n\n" + target,
                    "GVR Tools — Desinstalar",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            Directory.Delete(target, true);
            MessageBox.Show("Desinstalación completa. Reinicia Civil 3D.", "GVR Tools",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string GetTargetBundlePath() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk", "ApplicationPlugins", BundleName);

        private static IReadOnlyList<int> DetectCivil3DYears()
        {
            var found = new List<int>();
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            foreach (int year in SupportedYears)
            {
                string acadDir = Path.Combine(programFiles, "Autodesk", "AutoCAD " + year);
                if (File.Exists(Path.Combine(acadDir, "acad.exe")) &&
                    Directory.Exists(Path.Combine(acadDir, "C3D")))
                {
                    found.Add(year);
                }
            }

            return found;
        }

        private static IReadOnlyList<int> GetPackagedYears(string bundlePath)
        {
            string contents = Path.Combine(bundlePath, "Contents");
            if (!Directory.Exists(contents)) return Array.Empty<int>();

            return Directory.GetDirectories(contents)
                .Select(Path.GetFileName)
                .Select(name => int.TryParse(name, out int year) ? year : 0)
                .Where(year => year > 0)
                .OrderBy(year => year)
                .ToList();
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);

            foreach (string dir in Directory.GetDirectories(sourceDir))
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }
}
