using System.Windows;

namespace GvrTools.UI.Services
{
    /// <summary>
    /// Every window this add-in shows lives inside Revit's process, not inside a standalone WPF
    /// app -- there is no <c>App.xaml</c>/<c>Application.Run()</c> of our own. WPF's default
    /// <see cref="ShutdownMode.OnLastWindowClose"/> is meant for a real standalone app: it treats
    /// "the last tracked <see cref="Window"/> just closed" as "the whole program is done" and tears
    /// the shared <see cref="Application"/> down. Inside a host process that is the wrong signal --
    /// closing a secondary popup (e.g. the export-success summary) can end up counted as the "last"
    /// window if the main tool window's <see cref="Window.Owner"/> was never registered the same way,
    /// which reads to the user as "closing the success popup also closed the whole tool". Revit's own
    /// <see cref="Application"/> is shared by every add-in in the process, so this must be set
    /// defensively from every window we show (not once at startup) rather than assumed already safe.
    /// </summary>
    public static class WpfHostGuard
    {
        /// <summary>
        /// Forces <see cref="ShutdownMode.OnExplicitShutdown"/> on the current WPF <see cref="Application"/>
        /// so no window this add-in shows can ever cascade-close another just by being the last one
        /// WPF happens to be tracking. Cheap and safe to call every time (setting the same enum value
        /// repeatedly is a no-op) -- call from every window's constructor, right where the brand icon
        /// is set. Deliberately NOT cached behind a "did this already run" flag: called from a
        /// constructor, which runs before that window's own Show()/ShowDialog(), so for the very
        /// first window this add-in ever shows in a session, <see cref="Application.Current"/> can
        /// still be null at this point (WPF creates it lazily on the first Show()) -- a cached "already
        /// applied" flag would latch onto that early no-op and skip ever actually setting it.
        /// </summary>
        public static void EnsureExplicitShutdown()
        {
            // Application.Current is whatever WPF Application Revit itself (or the first add-in
            // to show a window) already created -- we never create our own, only adjust this one
            // shared setting on it. Null here just means no Application exists yet; the next window
            // shown (or this one, once WPF lazily creates it during its own Show()) will catch it.
            if (Application.Current != null)
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
    }
}
