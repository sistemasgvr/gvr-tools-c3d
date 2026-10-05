using System;
using GvrTools.Civil3D.Export;
using GvrTools.Civil3D.Export.Plotting;
using GvrTools.Core.IO;

namespace GvrTools.Tools.BatchExport.ViewModels
{
    /// <summary>One device of the "Impresora/trazador" combo.</summary>
    public sealed class PlotDeviceItem
    {
        public PlotDeviceItem(PlotDeviceInfo info)
        {
            Info = info;
        }

        public PlotDeviceInfo Info { get; }

        public string Name => Info.Name;

        /// <summary>Small tag shown next to the name, like the printer / plotter icons of the Plot dialog.</summary>
        public string KindLabel => Info.Kind == PlotDeviceKind.SystemPrinter ? "Windows" : "PC3";

        public override string ToString() => Name;
    }

    public enum PageSetupChoiceKind
    {
        Manual,
        LayoutOwn,
        Named
    }

    /// <summary>One entry of the "Configuración de página" combo.</summary>
    public sealed class PageSetupChoice
    {
        private PageSetupChoice(PageSetupChoiceKind kind, string label, NamedPageSetup setup)
        {
            Kind = kind;
            Label = label;
            Setup = setup;
        }

        public static readonly PageSetupChoice Manual =
            new PageSetupChoice(PageSetupChoiceKind.Manual, "<Configuración manual>", null);

        public static readonly PageSetupChoice LayoutOwn =
            new PageSetupChoice(PageSetupChoiceKind.LayoutOwn, "<Propia de cada presentación>", null);

        public static PageSetupChoice Named(NamedPageSetup setup) => new PageSetupChoice(
            PageSetupChoiceKind.Named,
            string.IsNullOrEmpty(setup.SourceLabel) ? setup.Name : $"{setup.Name}  ({setup.SourceLabel})",
            setup);

        public PageSetupChoiceKind Kind { get; }

        public string Label { get; }

        public NamedPageSetup Setup { get; }

        public override string ToString() => Label;
    }

    /// <summary>One entry of the paper combo: a GVR option or one of the device's sizes.</summary>
    public sealed class PaperChoice
    {
        public const string GvrGroup = "Opciones GVR";
        public const string DeviceGroup = "Tamaños del dispositivo";

        private PaperChoice(PlotPaperMode mode, IsoFullBleedSize isoSize, string canonicalName, string label, string group)
        {
            Mode = mode;
            IsoSize = isoSize;
            CanonicalName = canonicalName ?? string.Empty;
            Label = label;
            Group = group;
        }

        public static PaperChoice UseLayout() =>
            new PaperChoice(PlotPaperMode.UseLayout, IsoFullBleedSize.A4, null, "Tamaño de cada presentación", GvrGroup);

        public static PaperChoice Iso(IsoFullBleedSize size) =>
            new PaperChoice(PlotPaperMode.ForceIsoFullBleed, size, null,
                $"ISO full bleed {size} (según la orientación)", GvrGroup);

        public static PaperChoice Device(PlotMediaInfo media) =>
            new PaperChoice(PlotPaperMode.SelectFromDevice, IsoFullBleedSize.A4, media.CanonicalName, media.LocalName, DeviceGroup);

        public PlotPaperMode Mode { get; }

        public IsoFullBleedSize IsoSize { get; }

        public string CanonicalName { get; }

        public string Label { get; }

        /// <summary>Group header in the combo (CollectionViewSource grouping).</summary>
        public string Group { get; }

        public bool Matches(PlotPaperMode mode, IsoFullBleedSize isoSize, string canonicalName)
        {
            if (mode != Mode) return false;

            switch (mode)
            {
                case PlotPaperMode.ForceIsoFullBleed: return isoSize == IsoSize;
                case PlotPaperMode.SelectFromDevice:
                    return string.Equals(canonicalName, CanonicalName, StringComparison.OrdinalIgnoreCase);
                default: return true;
            }
        }

        public override string ToString() => Label;
    }

    /// <summary>One entry of the scale combo; <see cref="Scale"/> is null for "Personalizada".</summary>
    public sealed class ScaleChoice
    {
        public static readonly ScaleChoice Custom = new ScaleChoice(null);

        public ScaleChoice(PlotScale scale)
        {
            Scale = scale;
        }

        public PlotScale Scale { get; }

        public bool IsCustom => Scale == null;

        public string Label => Scale?.Name ?? "Personalizada";

        public override string ToString() => Label;
    }
}
