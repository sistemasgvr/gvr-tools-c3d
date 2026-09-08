using System;
using System.IO;
using GvrTools.Core.IO;
using Xunit;

namespace GvrTools.Core.Tests
{
    public sealed class ExportPathHelperTests
    {
        [Fact]
        public void ResolveWritableFolder_uses_first_writable_candidate()
        {
            string temp = Path.Combine(Path.GetTempPath(), "gvr-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);

            try
            {
                string resolved = ExportPathHelper.ResolveWritableFolder(
                    @"C:\Program Files\Autodesk\Revit 2021\Samples\blocked",
                    temp);

                Assert.Equal(temp, resolved);
            }
            finally
            {
                Directory.Delete(temp, recursive: true);
            }
        }

        [Fact]
        public void TryEnsureWritable_succeeds_for_temp_folder()
        {
            string temp = Path.Combine(Path.GetTempPath(), "gvr-export-" + Guid.NewGuid().ToString("N"));

            try
            {
                Assert.True(ExportPathHelper.TryEnsureWritable(temp, out string error), error);
                Assert.True(Directory.Exists(temp));
            }
            finally
            {
                if (Directory.Exists(temp))
                    Directory.Delete(temp, recursive: true);
            }
        }

        [Fact]
        public void AllocateUniqueDirectoryPath_returns_same_when_missing()
        {
            string root = Path.Combine(Path.GetTempPath(), "gvr-unique-" + Guid.NewGuid().ToString("N"));
            string desired = Path.Combine(root, "PDF_Demo");

            try
            {
                Assert.Equal(desired, ExportPathHelper.AllocateUniqueDirectoryPath(desired));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void AllocateUniqueDirectoryPath_appends_explorer_style_suffix()
        {
            string root = Path.Combine(Path.GetTempPath(), "gvr-unique-" + Guid.NewGuid().ToString("N"));
            string desired = Path.Combine(root, "PDF_Demo");
            Directory.CreateDirectory(desired);
            Directory.CreateDirectory(Path.Combine(root, "PDF_Demo (1)"));

            try
            {
                string allocated = ExportPathHelper.AllocateUniqueDirectoryPath(desired);
                Assert.Equal(Path.Combine(root, "PDF_Demo (2)"), allocated);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }
    }
}
