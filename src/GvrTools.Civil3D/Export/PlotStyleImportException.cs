using System;

namespace GvrTools.Civil3D.Export
{
    /// <summary>
    /// A plot style table (.ctb/.stb) the user picked could not be installed. The message is written
    /// for the end user and shown verbatim.
    /// </summary>
    public class PlotStyleImportException : Exception
    {
        public PlotStyleImportException(string message)
            : base(message)
        {
        }

        public PlotStyleImportException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    /// <summary>
    /// A table with that file name is already installed. Separate from its base type so the window
    /// can offer to replace it instead of just reporting a failure.
    /// </summary>
    public sealed class PlotStyleAlreadyExistsException : PlotStyleImportException
    {
        public PlotStyleAlreadyExistsException(string tableName, string destinationPath)
            : base($"Ya existe una tabla de estilos llamada \"{tableName}\".")
        {
            TableName = tableName;
            DestinationPath = destinationPath;
        }

        public string TableName { get; }

        public string DestinationPath { get; }
    }
}
