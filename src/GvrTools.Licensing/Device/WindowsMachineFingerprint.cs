using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace GvrTools.Licensing.Device
{
    /// <summary>
    /// Huella estable de la MÁQUINA: hash SHA-256 de MachineGuid + volumen sistema. Solo se envía el
    /// hex del hash, nunca el dato crudo.
    ///
    /// Deliberadamente NO incluye el SID del usuario de Windows actual (versión anterior de esta
    /// clase sí lo hacía). Con el SID en la mezcla, cualquiera con permisos de administrador en su
    /// propio PC (el caso normal de un usuario final) podía crear una segunda cuenta de Windows local
    /// y activar una licencia free nueva por cada cuenta creada en el MISMO hardware -- multiplicando
    /// asientos gratuitos sin ninguna manipulación de bajo nivel. Sin el SID, todas las cuentas de
    /// Windows de una misma máquina comparten una sola huella: para la licencia de pago esto es
    /// además más correcto (un dispositivo compartido por varias personas de la empresa a lo largo
    /// del tiempo sigue contando como UN dispositivo, no uno por persona que lo usó).
    ///
    /// ATENCIÓN -- impacto de migración: este cambio de fórmula hace que toda instalación YA
    /// activada reciba una huella distinta a partir de la próxima actualización del add-in (la huella
    /// vieja incluía el SID, la nueva no). El próximo activate/heartbeat de cada cliente existente se
    /// verá para el servidor como "dispositivo nuevo" -- puede tropezar con el límite de
    /// max_devices_per_user si esa persona ya tenía otro device activo bajo la huella vieja. Antes de
    /// desplegar esto a producción hace falta un plan de esa transición (p. ej. limpiar/migrar los
    /// devices existentes desde Admin, o dar una gracia temporal en el server), no solo el cambio de
    /// código del cliente.
    /// </summary>
    public sealed class WindowsMachineFingerprint : IMachineFingerprint
    {
        private string _cached;

        public string GetFingerprint()
        {
            if (_cached != null) return _cached;

            var sb = new StringBuilder(256);
            sb.Append(ReadMachineGuid());
            sb.Append('|');
            sb.Append(ReadSystemVolumeSerial());

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                    hex.Append(b.ToString("x2"));
                _cached = hex.ToString();
            }

            return _cached;
        }

        private static string ReadMachineGuid()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    return key?.GetValue("MachineGuid") as string ?? "no-machine-guid";
                }
            }
            catch
            {
                return "no-machine-guid";
            }
        }

        private static string ReadSystemVolumeSerial()
        {
            try
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (string.IsNullOrEmpty(root) || root.Length < 3)
                    return "no-volume";

                var drive = root.Substring(0, 3);
                if (GetVolumeInformation(drive, null, 0, out uint serial, out _, out _, null, 0))
                    return serial.ToString("X8");
            }
            catch
            {
                // fall through
            }

            return "no-volume";
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetVolumeInformation(
            string rootPathName,
            StringBuilder volumeNameBuffer,
            int volumeNameSize,
            out uint volumeSerialNumber,
            out uint maximumComponentLength,
            out uint fileSystemFlags,
            StringBuilder fileSystemNameBuffer,
            int fileSystemNameSize);
    }
}
