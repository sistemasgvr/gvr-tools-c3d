using System;
using System.Windows.Media;

namespace GvrTools.Civil3D.Ribbon
{
    /// <summary>
    /// Convenience base class so a tool only declares what actually differs from the defaults.
    /// A minimal tool is four overrides: <see cref="Id"/>, <see cref="Title"/>,
    /// <see cref="PanelName"/> and <see cref="CommandName"/>.
    /// </summary>
    public abstract class CivilToolBase : ICivilTool
    {
        public abstract string Id { get; }

        public abstract string Title { get; }

        public abstract string PanelName { get; }

        public abstract string CommandName { get; }

        public virtual int SortOrder => 100;

        public virtual string Tooltip => Title?.Replace(Environment.NewLine, " ").Replace("\n", " ");

        public virtual string LongDescription => null;

        public virtual ImageSource CreateIcon() => null;

        public virtual bool IsSupported() => true;

        /// <inheritdoc cref="ICivilTool.RequiredFeature"/>
        public virtual string RequiredFeature => null;
    }
}
