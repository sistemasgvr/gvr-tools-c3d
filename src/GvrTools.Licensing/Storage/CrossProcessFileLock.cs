using System;
using System.IO;
using System.Threading;

namespace GvrTools.Licensing.Storage
{
    /// <summary>
    /// Mutex nombrado a nivel de sistema operativo, para proteger un archivo compartido entre
    /// PROCESOS -- dos Revit.exe abiertos al mismo tiempo en la misma máquina, cada uno con su propio
    /// LicenseClient apuntando al mismo license.dat/usage-queue.json bajo %APPDATA%\GVR\GvrTools.
    ///
    /// El <c>lock</c> normal de C# (Monitor, vía el campo <c>_gate</c> de cada store) solo protege
    /// hilos DENTRO del mismo proceso -- dos procesos distintos tienen cada uno su propio objeto
    /// _gate en memoria, así que nunca se bloquean entre sí sin esto. Sin este mutex, dos procesos
    /// podían intercalar lectura-modificación-escritura clásica sobre el mismo archivo (A lee N
    /// ítems, B lee los mismos N antes de que A guarde, A guarda N+1, B guarda su propia copia N+1
    /// sin el ítem de A) y perder una escritura en silencio, sin ningún error visible.
    /// </summary>
    internal sealed class CrossProcessFileLock : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly bool _owned;

        /// <param name="filePath">
        /// Archivo a proteger. El nombre del mutex se deriva del NOMBRE de archivo (no de la ruta
        /// completa ni de GetHashCode(), cuyo resultado en .NET puede variar entre procesos) -- estos
        /// archivos siempre viven en la misma carpeta fija con nombres constantes, así que esto es
        /// suficiente para que todos los procesos de GVR Tools en la máquina coincidan en el mismo
        /// nombre de mutex para el mismo archivo.
        /// </param>
        public CrossProcessFileLock(string filePath, TimeSpan timeout)
        {
            string fileName = Path.GetFileName(filePath ?? string.Empty);
            string name = "Global\\GvrTools_" + (string.IsNullOrEmpty(fileName) ? "default" : fileName);

            try
            {
                _mutex = new Mutex(false, name);
            }
            catch
            {
                // Nombre inválido u otro fallo de creación del mutex (muy improbable con nombres de
                // archivo reales): degrada a "sin protección cruzada de proceso" en vez de tumbar la
                // operación -- sigue siendo estrictamente mejor que no tener este fix.
                _mutex = null;
                _owned = false;
                return;
            }

            try
            {
                _owned = _mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                // El proceso dueño anterior murió sin liberar (p. ej. Revit se cerró a la fuerza a
                // mitad de una escritura) -- el mutex sigue siendo válido para tomarse; el archivo en
                // sí puede haber quedado a medio escribir, pero eso ya lo cubre el try/catch de cada
                // store alrededor de su propia lectura/escritura.
                _owned = true;
            }
        }

        /// <summary>true si se obtuvo el mutex dentro del timeout; false si se degradó sin protección.</summary>
        public bool Acquired => _owned;

        public void Dispose()
        {
            try
            {
                if (_owned) _mutex?.ReleaseMutex();
            }
            catch
            {
                // Best effort.
            }

            _mutex?.Dispose();
        }
    }
}
