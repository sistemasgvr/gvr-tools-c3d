using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GvrTools.UI.Icons
{
    /// <summary>
    /// Central lookup for the suite's brand imagery. Any window or ribbon button that wants the GVR
    /// escutcheon asks for it here, so if the image ever changes location or format there is a
    /// single place to update.
    /// </summary>
    public static class BrandIcons
    {
        /// <summary>Pack URI of the GVR shield. Usable from XAML and code alike.</summary>
        public const string EscudoUri = "pack://application:,,,/GvrTools.UI;component/Icons/Escudo_GVR.png";

        /// <summary>Frozen image source of the GVR shield.</summary>
        public static ImageSource Escudo => LazyEscudo.Value;

        private static readonly Lazy<ImageSource> LazyEscudo = new Lazy<ImageSource>(() =>
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(EscudoUri, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        });
    }
}
