using System;
using System.IO;

namespace CubeHaul.Tests.EditMode
{
    /// <summary>
    /// Finds the Unity project root without UnityEngine: in the Editor the working directory is the project root;
    /// in plain .NET (Tools/dotnet) the tests run from a build folder below it.
    /// </summary>
    static class ProjectPaths
    {
        public static readonly string Root = Find();

        static string Find()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                    if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "_CubeHaul"))) return dir.FullName;
            }
            throw new InvalidOperationException("Assets/_CubeHaul not found above " + Directory.GetCurrentDirectory());
        }

        public static string Of(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
