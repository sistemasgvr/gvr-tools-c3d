using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using GvrTools.Civil3D.Ribbon;
using GvrTools.Core.Diagnostics;
using SysException = System.Exception;

namespace GvrTools.App.Ribbon
{
    /// <summary>
    /// Finds the tools to put on the ribbon.
    ///
    /// Discovery is by convention: every assembly named <c>GvrTools.Tools.*.dll</c> sitting next to
    /// the add-in is scanned for public, concrete <see cref="ICivilTool"/> implementations with a
    /// parameterless constructor. Identical strategy to the Revit build's <c>ToolCatalog</c>.
    ///
    /// Tool DLLs are loaded through <see cref="SystemObjects.DynamicLinker.LoadModule"/> (same path
    /// NETLOAD uses) so AutoCAD registers their <c>[CommandMethod]</c> entries. Plain
    /// <c>Assembly.LoadFrom</c> alone is not enough when the host was the only module AutoCAD was
    /// asked to load.
    /// </summary>
    public static class ToolCatalog
    {
        private const string ToolAssemblyPattern = "GvrTools.Tools.*.dll";

        public static IReadOnlyList<ICivilTool> Discover(ILog log)
        {
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var tools = new List<ICivilTool>();

            foreach (Assembly assembly in LoadToolAssemblies(directory, log))
                tools.AddRange(InstantiateTools(assembly, log));

            return tools
                .OrderBy(tool => tool.PanelName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(tool => tool.SortOrder)
                .ThenBy(tool => tool.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static IEnumerable<Assembly> LoadToolAssemblies(string directory, ILog log)
        {
            string[] files;

            try
            {
                files = Directory.GetFiles(directory, ToolAssemblyPattern);
            }
            catch (SysException ex)
            {
                log.Error($"No se pudo listar las herramientas en '{directory}'.", ex);
                yield break;
            }

            foreach (string file in files)
            {
                Assembly assembly = TryLoadToolAssembly(file, log);
                if (assembly != null) yield return assembly;
            }
        }

        private static Assembly TryLoadToolAssembly(string file, ILog log)
        {
            string fullPath = Path.GetFullPath(file);
            string fileName = Path.GetFileName(fullPath);

            try
            {
                // Registers [CommandMethod] / [CommandClass] with AutoCAD the same way NETLOAD does.
                SystemObjects.DynamicLinker.LoadModule(fullPath, false, false);
                log.Info($"Módulo de herramienta registrado con AutoCAD: {fileName}");
            }
            catch (SysException ex)
            {
                // Already loaded as a project dependency, or LoadModule rejected a re-load: fall
                // through to Assembly.LoadFrom so ribbon discovery still works.
                log.Warn($"LoadModule('{fileName}') no aplicó ({ex.Message}); se usa Assembly.LoadFrom.");
            }

            try
            {
                return Assembly.LoadFrom(fullPath);
            }
            catch (SysException ex)
            {
                log.Error($"No se pudo cargar la herramienta '{fileName}'.", ex);
                return null;
            }
        }

        private static IEnumerable<ICivilTool> InstantiateTools(Assembly assembly, ILog log)
        {
            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                log.Warn($"'{assembly.GetName().Name}' se cargó parcialmente: {ex.Message}");
                types = ex.Types.Where(type => type != null).ToArray();
            }
            catch (SysException ex)
            {
                log.Error($"No se pudieron leer los tipos de '{assembly.GetName().Name}'.", ex);
                yield break;
            }

            foreach (Type type in types.Where(IsInstantiableTool))
            {
                ICivilTool tool = null;

                try
                {
                    tool = (ICivilTool)Activator.CreateInstance(type);

                    if (!tool.IsSupported())
                    {
                        log.Info($"La herramienta '{tool.Id}' no es compatible con esta versión; se omite.");
                        tool = null;
                    }
                }
                catch (SysException ex)
                {
                    log.Error($"No se pudo crear la herramienta '{type.FullName}'.", ex);
                }

                if (tool != null) yield return tool;
            }
        }

        private static bool IsInstantiableTool(Type type) =>
            typeof(ICivilTool).IsAssignableFrom(type) &&
            !type.IsAbstract &&
            !type.IsInterface &&
            type.IsPublic &&
            type.GetConstructor(Type.EmptyTypes) != null;
    }
}
