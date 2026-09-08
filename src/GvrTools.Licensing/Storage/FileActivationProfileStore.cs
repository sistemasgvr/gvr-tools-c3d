using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace GvrTools.Licensing.Storage
{
    /// <summary>
    /// Recuerda el último nombre/correo usados en activación (solo UX, no es credencial).
    /// </summary>
    public sealed class FileActivationProfileStore
    {
        private readonly string _path;

        public FileActivationProfileStore(string path = null)
        {
            if (path != null)
            {
                _path = path;
                return;
            }

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GVR", "GvrTools");
            _path = Path.Combine(dir, "activation-profile.json");
        }

        public ActivationProfile Load()
        {
            try
            {
                if (!File.Exists(_path))
                    return new ActivationProfile();

                using (var stream = File.OpenRead(_path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(ActivationProfile));
                    return (ActivationProfile)serializer.ReadObject(stream) ?? new ActivationProfile();
                }
            }
            catch
            {
                return new ActivationProfile();
            }
        }

        public void Save(string fullName, string email)
        {
            try
            {
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var profile = new ActivationProfile
                {
                    FullName = fullName?.Trim() ?? string.Empty,
                    Email = email?.Trim() ?? string.Empty
                };

                using (var stream = File.Create(_path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(ActivationProfile));
                    serializer.WriteObject(stream, profile);
                }
            }
            catch
            {
                // Best effort.
            }
        }

        public sealed class ActivationProfile
        {
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
        }
    }
}
