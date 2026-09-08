using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using GvrTools.Licensing.Http.Dto;

namespace GvrTools.Licensing.Storage
{
    /// <summary>
    /// Cola de UsageEvent pendientes de enviar (consumo offline). Misma carpeta que license.dat.
    /// </summary>
    public sealed class FileUsageQueueStore
    {
        private readonly string _path;
        private readonly object _gate = new object();

        public FileUsageQueueStore(string path = null)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GVR", "GvrTools");
            Directory.CreateDirectory(dir);
            _path = path ?? Path.Combine(dir, "usage-queue.json");
        }

        // Timeout del mutex entre procesos: generoso porque una escritura de este archivo es
        // milisegundos, nunca debería tardar más salvo que otro proceso quedara colgado sosteniéndolo.
        private static readonly TimeSpan CrossProcessTimeout = TimeSpan.FromSeconds(5);

        public void Enqueue(UsageEventDto item)
        {
            using (new CrossProcessFileLock(_path, CrossProcessTimeout))
            lock (_gate)
            {
                var list = Load();
                list.Add(item);
                Save(list);
            }
        }

        public List<UsageEventDto> PeekAll()
        {
            using (new CrossProcessFileLock(_path, CrossProcessTimeout))
            lock (_gate)
            {
                return Load();
            }
        }

        /// <summary>Devuelve todos los eventos pendientes y vacía la cola de forma atómica.</summary>
        public List<UsageEventDto> TakeAll()
        {
            using (new CrossProcessFileLock(_path, CrossProcessTimeout))
            lock (_gate)
            {
                var list = Load();
                Save(new List<UsageEventDto>());
                return list;
            }
        }

        /// <summary>Inserta eventos al inicio de la cola (p. ej. reintento tras fallo de red).</summary>
        public void PrependAll(IReadOnlyList<UsageEventDto> items)
        {
            if (items == null || items.Count == 0)
                return;

            using (new CrossProcessFileLock(_path, CrossProcessTimeout))
            lock (_gate)
            {
                var current = Load();
                var merged = new List<UsageEventDto>(items.Count + current.Count);
                merged.AddRange(items);
                merged.AddRange(current);
                Save(merged);
            }
        }

        public void ReplaceAll(List<UsageEventDto> remaining)
        {
            using (new CrossProcessFileLock(_path, CrossProcessTimeout))
            lock (_gate)
            {
                Save(remaining ?? new List<UsageEventDto>());
            }
        }

        /// <summary>
        /// Quita un único evento por Id, releyendo el archivo primero -- a diferencia de ReplaceAll
        /// (que sobreescribe la cola entera con una lista dada por el llamador, potencialmente
        /// obsoleta si algo más encoló un evento nuevo mientras tanto), esto siempre parte del
        /// contenido ACTUAL en disco bajo el mismo lock, así que nunca puede perder un Enqueue()
        /// concurrente de otro hilo/tarea (p. ej. otra lámina exportándose mientras un flush está en
        /// curso en segundo plano).
        /// </summary>
        public void RemoveById(Guid eventId)
        {
            using (new CrossProcessFileLock(_path, CrossProcessTimeout))
            lock (_gate)
            {
                var current = Load();
                current.RemoveAll(x => x.EventId == eventId);
                Save(current);
            }
        }

        public void Clear()
        {
            using (new CrossProcessFileLock(_path, CrossProcessTimeout))
            lock (_gate)
            {
                if (File.Exists(_path))
                    File.Delete(_path);
            }
        }

        private List<UsageEventDto> Load()
        {
            if (!File.Exists(_path))
                return new List<UsageEventDto>();

            try
            {
                var bytes = File.ReadAllBytes(_path);
                using (var stream = new MemoryStream(bytes))
                {
                    var serializer = new DataContractJsonSerializer(typeof(UsageQueueFile));
                    var file = (UsageQueueFile)serializer.ReadObject(stream);
                    return file?.Items ?? new List<UsageEventDto>();
                }
            }
            catch
            {
                return new List<UsageEventDto>();
            }
        }

        private void Save(List<UsageEventDto> items)
        {
            var file = new UsageQueueFile { Items = items };
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(UsageQueueFile));
                serializer.WriteObject(stream, file);
                File.WriteAllBytes(_path, stream.ToArray());
            }
        }

        [DataContract]
        private sealed class UsageQueueFile
        {
            [DataMember]
            public List<UsageEventDto> Items { get; set; }
        }
    }
}
