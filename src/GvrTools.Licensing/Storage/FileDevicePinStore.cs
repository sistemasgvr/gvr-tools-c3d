using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace GvrTools.Licensing.Storage
{
    /// <summary>
    /// Recuerda el DeviceId (GUID interno del servidor, ver <c>EntitlementBlob.DeviceId</c>) de la
    /// última respuesta ONLINE genuina de activate/heartbeat para esta instalación.
    ///
    /// Vive en su PROPIO archivo, separado de <c>license.dat</c>, a propósito: license.dat guarda el
    /// blob firmado completo, así que si alguien copia SOLO ese archivo a otra máquina para "clonar"
    /// la licencia sin conexión, esta marca no viaja con la copia -- EntitlementService.TryApplySignedBlob
    /// compara el DeviceId del blob copiado contra esta marca local y lo rechaza si no coincide. (Copiar
    /// la carpeta ENTERA, incluyendo este archivo, sigue sin poder evitarse por medios puramente locales
    /// -- ninguna instalación de escritorio puede protegerse de eso sin depender de hardware, que este
    /// producto no requiere. Lo que sí evita es el caso más simple: copiar nada más license.dat.)
    ///
    /// Mientras el archivo no exista todavía (primera vez, o una instalación existente de antes de este
    /// mecanismo) no hay nada contra qué comparar -- se acepta una vez sin marca y recién ahí se fija,
    /// igual que cualquier esquema de "trust on first use".
    /// </summary>
    public sealed class FileDevicePinStore
    {
        private readonly string _path;

        public FileDevicePinStore(string path = null)
        {
            if (path != null)
            {
                _path = path;
                return;
            }

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GVR", "GvrTools");
            _path = Path.Combine(dir, "device-pin.dat");
        }

        // Ver CrossProcessFileLock: mismo criterio que FileLicenseCacheStore/FileUsageQueueStore.
        private static readonly TimeSpan CrossProcessTimeout = TimeSpan.FromSeconds(5);

        /// <summary>DeviceId fijado, o null si no hay ninguno todavía (nunca se llamó a Save, o el archivo es ilegible).</summary>
        public string Load()
        {
            try
            {
                using (new CrossProcessFileLock(_path, CrossProcessTimeout))
                {
                    if (!File.Exists(_path)) return null;

                    using (var stream = File.OpenRead(_path))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(Pin));
                        var pin = (Pin)serializer.ReadObject(stream);
                        return string.IsNullOrEmpty(pin?.DeviceId) ? null : pin.DeviceId;
                    }
                }
            }
            catch
            {
                // Igual criterio que el resto de los stores de este namespace: un archivo corrupto o
                // inaccesible nunca debe tumbar el arranque -- simplemente se trata como "sin marca".
                return null;
            }
        }

        public void Save(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;

            try
            {
                using (new CrossProcessFileLock(_path, CrossProcessTimeout))
                {
                    string dir = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    using (var stream = File.Create(_path))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(Pin));
                        serializer.WriteObject(stream, new Pin { DeviceId = deviceId });
                    }
                }
            }
            catch
            {
                // Best effort -- perder esta escritura solo significa quedar "sin marca" hasta el
                // próximo contacto online exitoso, nunca debe tumbar la activación en curso.
            }
        }

        private sealed class Pin
        {
            public string DeviceId { get; set; } = string.Empty;
        }
    }
}
