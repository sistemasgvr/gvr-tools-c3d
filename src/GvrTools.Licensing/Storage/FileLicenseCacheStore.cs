using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace GvrTools.Licensing.Storage
{
    /// <summary>
    /// Persiste AccessToken + blob firmado en %APPDATA%\GVR\GvrTools\license.dat
    /// (envoltorio local no firmado; la verificación ECDSA corre solo sobre EntitlementJson).
    /// </summary>
    public sealed class FileLicenseCacheStore : ILicenseCacheStore
    {
        private readonly string _path;

        public FileLicenseCacheStore(string path = null)
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GVR", "GvrTools");
            Directory.CreateDirectory(dir);
            _path = path ?? System.IO.Path.Combine(dir, "license.dat");
        }

        public string FilePath => _path;

        // Ver CrossProcessFileLock: dos Revit.exe abiertos a la vez en la misma máquina, cada uno con
        // su propio LicenseClient, comparten este mismo archivo -- sin este mutex entre procesos
        // podían intercalar lectura-modificación-escritura y perderse cambios en silencio.
        private static readonly TimeSpan CrossProcessTimeout = TimeSpan.FromSeconds(5);

        public bool TryLoad(out string rawJson, out byte[] signature)
        {
            rawJson = null;
            signature = null;

            if (!TryLoadEnvelope(out var envelope))
                return false;

            if (string.IsNullOrEmpty(envelope.EntitlementJson) ||
                string.IsNullOrEmpty(envelope.EntitlementSignatureBase64))
                return false;

            try
            {
                rawJson = envelope.EntitlementJson;
                signature = Convert.FromBase64String(envelope.EntitlementSignatureBase64);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public bool TryLoadEnvelope(out LicenseCacheEnvelope envelope)
        {
            using (new CrossProcessFileLock(_path, CrossProcessTimeout))
            {
                return TryLoadEnvelopeUnlocked(out envelope);
            }
        }

        /// <summary>
        /// Lee-modifica-escribe bajo UN SOLO acquire del mutex entre procesos (no dos, uno para leer
        /// y otro aparte para escribir) -- así ningún otro proceso puede colarse justo entre la
        /// lectura del existente y la escritura del actualizado.
        /// </summary>
        public void Save(string rawJson, byte[] signature)
        {
            using (new CrossProcessFileLock(_path, CrossProcessTimeout))
            {
                if (!TryLoadEnvelopeUnlocked(out var existing))
                    existing = new LicenseCacheEnvelope();

                existing.EntitlementJson = rawJson;
                existing.EntitlementSignatureBase64 = Convert.ToBase64String(signature);
                WriteEnvelopeUnlocked(existing);
            }
        }

        public void SaveEnvelope(LicenseCacheEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));

            using (new CrossProcessFileLock(_path, CrossProcessTimeout))
            {
                WriteEnvelopeUnlocked(envelope);
            }
        }

        public void Clear()
        {
            using (new CrossProcessFileLock(_path, CrossProcessTimeout))
            {
                if (File.Exists(_path))
                    File.Delete(_path);
            }
        }

        private bool TryLoadEnvelopeUnlocked(out LicenseCacheEnvelope envelope)
        {
            envelope = null;
            if (!File.Exists(_path)) return false;

            try
            {
                var bytes = File.ReadAllBytes(_path);
                using (var stream = new MemoryStream(bytes))
                {
                    var serializer = new DataContractJsonSerializer(typeof(LicenseCacheEnvelope));
                    envelope = (LicenseCacheEnvelope)serializer.ReadObject(stream);
                }

                return envelope != null && !string.IsNullOrEmpty(envelope.EntitlementJson);
            }
            catch
            {
                envelope = null;
                return false;
            }
        }

        private void WriteEnvelopeUnlocked(LicenseCacheEnvelope envelope)
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(LicenseCacheEnvelope));
                serializer.WriteObject(stream, envelope);
                File.WriteAllBytes(_path, stream.ToArray());
            }
        }
    }

    [DataContract]
    public sealed class LicenseCacheEnvelope
    {
        [DataMember]
        public string AccessToken { get; set; }

        [DataMember]
        public string EntitlementJson { get; set; }

        [DataMember]
        public string EntitlementSignatureBase64 { get; set; }

        /// <summary>
        /// Consumo local de cuota (TryConsume/SetRemaining) todavía no confirmado por el servidor,
        /// APARTE del blob firmado -- ver el comentario en EntitlementService.Changed para el porqué.
        /// No forma parte del contenido firmado; se reaplica en la carga solo para RECORTAR remaining,
        /// nunca para aumentarlo, así que un archivo manipulado o ausente aquí como mucho vuelve al
        /// comportamiento previo a este fix (perder el consumo local en cada reinicio), nunca a
        /// otorgar más cuota de la que el servidor firmó.
        /// </summary>
        [DataMember]
        public Dictionary<string, string> LocalOverrides { get; set; }

        /// <summary>
        /// Piso de reloj monótono (ISO 8601 UTC) contra el retroceso del reloj del sistema -- ver
        /// EntitlementService.AdvanceClockFloor. Se actualiza en cada carga/sincronización exitosa con
        /// el "ahora" más alto visto hasta el momento; nunca se vuelve a bajar.
        /// </summary>
        [DataMember]
        public string LastObservedUtc { get; set; }
    }
}
