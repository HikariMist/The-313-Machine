using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HikariForge.M313.Core;

namespace HikariForge.M313.Tests
{
    /// <summary>Loads content JSON straight from disk; works under Unity Test Runner and dotnet test.</summary>
    public static class TestContent
    {
        private static ContentDatabase _db;

        public static string ProjectRoot()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "_Project"))) return dir.FullName;
                    dir = dir.Parent;
                }
            }
            throw new DirectoryNotFoundException("Could not locate the Unity project root (Assets/_Project).");
        }

        public static string ContentDir => Path.Combine(ProjectRoot(), "Assets", "_Project", "Resources", "Content");

        public static ContentDatabase Db => _db ?? (_db = ContentDatabase.Load(name =>
        {
            var path = Path.Combine(ContentDir, name + ".json");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }));
    }
}
