using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace GvrTools.Licensing.Config
{
    /// <summary>
    /// Base URL del License API. Override en %APPDATA%\GVR\GvrTools\license-config.json
    /// para apuntar a un servidor local sin redeploy (p. ej. http://localhost:5299).
    /// </summary>
    public static class LicenseApiSettings
    {
        public const string DefaultBaseUrl = "https://tools.proyectosgvr.com";

        public static string ResolveBaseUrl()
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GVR", "GvrTools", "license-config.json");

                if (!File.Exists(path))
                    return DefaultBaseUrl;

                var json = File.ReadAllText(path);
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var serializer = new DataContractJsonSerializer(typeof(LicenseConfigFile));
                    var file = (LicenseConfigFile)serializer.ReadObject(stream);
                    if (!string.IsNullOrWhiteSpace(file?.BaseUrl))
                        return file.BaseUrl.Trim().TrimEnd('/');
                }
            }
            catch
            {
                // fall through to default
            }

            return DefaultBaseUrl;
        }

        [DataContract]
        private sealed class LicenseConfigFile
        {
            [DataMember(Name = "baseUrl")]
            public string BaseUrl { get; set; }

            [DataMember(Name = "BaseUrl")]
            public string BaseUrlPascal
            {
                get => BaseUrl;
                set
                {
                    if (!string.IsNullOrWhiteSpace(value))
                        BaseUrl = value;
                }
            }
        }
    }
}
