using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace GvrTools.Core.Settings
{
    /// <summary>
    /// Stores each settings class as a flat <c>key=value</c> text file under
    /// <c>%APPDATA%\GVR\GvrTools\</c>, mapping public read/write properties by reflection.
    ///
    /// Why not JSON: a Revit add-in shares its process with Revit, which already loads its own
    /// versions of the usual serialisation libraries. Shipping another copy is a well known source
    /// of assembly-binding failures, and a handful of flat scalar preferences per tool simply does
    /// not justify that risk. Supported property types are string, bool, int, double and enum;
    /// anything else is skipped (and reported through the log).
    /// </summary>
    public sealed class FlatFileSettingsStore : ISettingsStore
    {
        private readonly string _directory;

        public FlatFileSettingsStore(string directory = null)
        {
            _directory = directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GVR", "GvrTools");
        }

        public T Load<T>(string key) where T : class, new()
        {
            var settings = new T();

            try
            {
                string path = PathFor(key);
                if (!File.Exists(path)) return settings;

                Dictionary<string, string> values = ReadPairs(path);

                foreach (PropertyInfo property in WritableProperties(typeof(T)))
                {
                    if (!values.TryGetValue(property.Name, out string raw)) continue;
                    if (TryConvert(raw, property.PropertyType, out object converted))
                        property.SetValue(settings, converted, null);
                }
            }
            catch
            {
                // Unreadable or corrupt settings simply mean "start from the defaults".
                return new T();
            }

            return settings;
        }

        public void Save<T>(string key, T value) where T : class
        {
            if (value == null) return;

            try
            {
                Directory.CreateDirectory(_directory);

                var sb = new StringBuilder();
                foreach (PropertyInfo property in WritableProperties(typeof(T)))
                {
                    if (!IsSupported(property.PropertyType)) continue;

                    object raw = property.GetValue(value, null);
                    sb.Append(property.Name).Append('=').AppendLine(Format(raw));
                }

                File.WriteAllText(PathFor(key), sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Remembering preferences is a convenience, never a reason to fail an operation.
            }
        }

        private string PathFor(string key) =>
            Path.Combine(_directory, Naming.PathSanitizer.SanitizeFileName(key, "settings") + ".settings");

        private static IEnumerable<PropertyInfo> WritableProperties(Type type)
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                    yield return property;
            }
        }

        private static Dictionary<string, string> ReadPairs(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string line in File.ReadAllLines(path))
            {
                int separator = line.IndexOf('=');
                if (separator <= 0) continue;

                values[line.Substring(0, separator)] = line.Substring(separator + 1);
            }

            return values;
        }

        private static bool IsSupported(Type type) =>
            type == typeof(string) || type == typeof(bool) || type == typeof(int) ||
            type == typeof(double) || type.IsEnum;

        private static string Format(object value)
        {
            if (value == null) return string.Empty;
            if (value is bool flag) return flag ? "true" : "false";
            if (value is int number) return number.ToString(CultureInfo.InvariantCulture);
            if (value is double real) return real.ToString("R", CultureInfo.InvariantCulture);

            return value.ToString();
        }

        private static bool TryConvert(string raw, Type type, out object value)
        {
            value = null;

            if (type == typeof(string))
            {
                value = raw;
                return true;
            }

            if (type == typeof(bool) && bool.TryParse(raw, out bool flag))
            {
                value = flag;
                return true;
            }

            if (type == typeof(int) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                value = number;
                return true;
            }

            if (type == typeof(double) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double real))
            {
                value = real;
                return true;
            }

            if (type.IsEnum && !string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    value = Enum.Parse(type, raw, true);
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            return false;
        }
    }
}
