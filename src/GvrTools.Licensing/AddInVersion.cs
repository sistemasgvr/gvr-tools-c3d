using System;
using System.Reflection;

namespace GvrTools.Licensing
{
    /// <summary>Versión del add-in para /v1/updates/check (AssemblyInformationalVersion).</summary>
    public static class AddInVersion
    {
        public static string Current
        {
            get
            {
                try
                {
                    var asm = typeof(AddInVersion).Assembly;
                    var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                    if (!string.IsNullOrWhiteSpace(info))
                    {
                        // Quitar sufijo de commit (+hash) si el SDK lo añade.
                        var plus = info.IndexOf('+');
                        return plus > 0 ? info.Substring(0, plus) : info;
                    }

                    var v = asm.GetName().Version;
                    if (v != null) return v.ToString(3);
                }
                catch
                {
                    // fall through
                }

                return "0.0.0";
            }
        }
    }
}
