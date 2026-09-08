using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using GvrTools.Civil3D.Model;
using GvrTools.Core.Batch;
using GvrTools.Core.Diagnostics;

namespace GvrTools.Civil3D.Export
{
    /// <summary>Format-specific options. Each engine declares the concrete type it expects.</summary>
    public interface IExportFormatSettings
    {
        ExportFormat Format { get; }
    }

    /// <summary>Everything one export run needs to know.</summary>
    public sealed class ExportRequest
    {
        public ExportRequest(
            Database database,
            string destinationFolder,
            string namingPattern,
            IExportFormatSettings settings,
            DrawingSnapshot drawing,
            ILog log = null,
            Document document = null)
        {
            Database = database;
            DestinationFolder = destinationFolder;
            NamingPattern = namingPattern;
            Settings = settings;
            Drawing = drawing;
            Log = log ?? NullLog.Instance;
            Document = document;
        }

        public Database Database { get; }

        /// <summary>The document being exported. Needed to lock the drawing and switch the active
        /// layout (AutoCAD refuses to plot a layout that is not current). May be null in tests.</summary>
        public Document Document { get; }

        public string DestinationFolder { get; }

        public string NamingPattern { get; }

        public IExportFormatSettings Settings { get; }

        public DrawingSnapshot Drawing { get; }

        public ILog Log { get; }

        public T SettingsAs<T>() where T : class, IExportFormatSettings
        {
            if (Settings is T typed) return typed;

            throw new ExportSetupException(
                $"Las opciones recibidas ({Settings?.GetType().Name ?? "ninguna"}) no corresponden al formato solicitado ({typeof(T).Name}).");
        }
    }

    /// <summary>
    /// Produces files for one format. Adding a format means adding one implementation of this
    /// interface and registering it in <see cref="ExportEngineCatalog"/>.
    /// </summary>
    public interface IExportEngine
    {
        ExportFormat Format { get; }

        /// <summary>Short description shown in the window of how this engine writes files.</summary>
        string StrategyDescription { get; }

        /// <param name="totalItems">How many layouts the run will export in total. Needed so a
        /// combined multi-page PDF knows which page is the last.</param>
        /// <exception cref="ExportSetupException">Nothing can be exported; message is user-facing.</exception>
        IExportSession BeginSession(ExportRequest request, int totalItems);
    }

    /// <summary>One export run. Not reusable, and always disposed by the caller.</summary>
    public interface IExportSession : IDisposable
    {
        /// <summary>
        /// Exports a single layout. Must not throw for a per-layout problem: return a failed
        /// <see cref="BatchItemResult"/> instead so the rest of the batch continues.
        /// </summary>
        BatchItemResult Export(LayoutSnapshot layout);
    }

    /// <summary>The engines available in this build.</summary>
    public sealed class ExportEngineCatalog
    {
        private readonly Dictionary<ExportFormat, IExportEngine> _engines = new Dictionary<ExportFormat, IExportEngine>();

        public ExportEngineCatalog(IEnumerable<IExportEngine> engines)
        {
            foreach (IExportEngine engine in engines)
            {
                if (engine != null) _engines[engine.Format] = engine;
            }
        }

        public static ExportEngineCatalog CreateDefault() => new ExportEngineCatalog(new IExportEngine[]
        {
            new Pdf.PdfExportEngine()
        });

        public IEnumerable<ExportFormat> SupportedFormats => _engines.Keys;

        public IExportEngine Resolve(ExportFormat format)
        {
            if (_engines.TryGetValue(format, out IExportEngine engine)) return engine;

            throw new ExportSetupException(
                $"Esta versión del complemento no puede exportar a {ExportFormatInfo.Label(format)}.");
        }
    }
}
