using System;
using System.Collections.Generic;
using System.IO;

namespace QVM_Editor
{
    // Describes the %APPDATA%\QEditor folder layout with platform-correct separators.
    internal sealed class AppDataLayout
    {
        internal readonly string AppDataPath;
        internal readonly string EditorPath;
        internal readonly string CompilerPath;
        internal readonly string KeywordsPath;

        internal AppDataLayout(string appDataPath, string editorFolderName, string compilerFolderName, string keywordsFileName)
        {
            AppDataPath = appDataPath;
            EditorPath = Path.Combine(appDataPath, editorFolderName);
            CompilerPath = Path.Combine(EditorPath, compilerFolderName);
            KeywordsPath = Path.Combine(EditorPath, keywordsFileName);
        }
    }

    internal sealed class AppDataBootstrapResult
    {
        internal bool Success { get; set; }
        internal AppDataLayout Layout { get; set; }
        internal string ErrorPath { get; set; }
        internal string ErrorMessage { get; set; }
        internal bool CompilerRestored { get; set; }
        internal bool CompilerFilesMissing { get; set; }
        internal bool KeywordsRestored { get; set; }
        internal List<string> Warnings { get; private set; }

        internal AppDataBootstrapResult()
        {
            Warnings = new List<string>();
        }

        internal static AppDataBootstrapResult Failure(string path, string message)
        {
            return new AppDataBootstrapResult
            {
                Success = false,
                ErrorPath = path,
                ErrorMessage = message
            };
        }
    }

    // Repairs the %APPDATA%\QEditor layout before the editor checks it.
    // Kept free of UI dependencies so the file/IO behaviour can be unit tested.
    internal static class AppDataBootstrap
    {
        internal const string DefaultEditorFolderName = "QEditor";
        internal const string DefaultCompilerFolderName = "QCompiler";
        internal const string DefaultKeywordsFileName = "keywords.txt";

        // ApplicationData is the roaming AppData folder on Windows. Fall back to the
        // %APPDATA% variable and then to the roaming profile when the API returns "".
        internal static string ResolveAppDataPath()
        {
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!String.IsNullOrWhiteSpace(folder)) return folder;

            string environmentPath = Environment.GetEnvironmentVariable("APPDATA");
            if (!String.IsNullOrWhiteSpace(environmentPath)) return environmentPath;

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!String.IsNullOrWhiteSpace(profile)) return Path.Combine(profile, "AppData", "Roaming");

            return null;
        }

        internal static AppDataBootstrapResult Ensure(
            string appDataPath,
            IEnumerable<string> sourceEditorPaths,
            string editorFolderName = DefaultEditorFolderName,
            string compilerFolderName = DefaultCompilerFolderName,
            string keywordsFileName = DefaultKeywordsFileName)
        {
            if (String.IsNullOrWhiteSpace(appDataPath))
            {
                return AppDataBootstrapResult.Failure("AppData",
                    "The roaming AppData folder could not be located. Check the APPDATA environment variable and your Windows user profile.");
            }

            var layout = new AppDataLayout(appDataPath, editorFolderName, compilerFolderName, keywordsFileName);
            var result = new AppDataBootstrapResult { Layout = layout };

            try
            {
                Directory.CreateDirectory(layout.EditorPath);
                Directory.CreateDirectory(layout.CompilerPath);
            }
            catch (Exception ex)
            {
                return AppDataBootstrapResult.Failure(
                    Directory.Exists(layout.EditorPath) ? layout.CompilerPath : layout.EditorPath,
                    "Cannot create the AppData folders: " + ex.Message);
            }

            // Migrate the bundle shipped next to the executable. Copying (rather than
            // Directory.Move) works across volumes and does not delete the user's files
            // when the destination already exists but is incomplete.
            if (sourceEditorPaths != null)
            {
                foreach (string source in sourceEditorPaths)
                {
                    if (String.IsNullOrWhiteSpace(source) || !Directory.Exists(source)) continue;
                    if (IsSamePath(source, layout.EditorPath)) continue;

                    RestoreFrom(source, layout, compilerFolderName, keywordsFileName, result);
                    break;
                }
            }

            result.CompilerFilesMissing = Directory.GetFileSystemEntries(layout.CompilerPath).Length == 0;
            result.Success = true;
            return result;
        }

        private static void RestoreFrom(string sourceEditorPath, AppDataLayout layout,
            string compilerFolderName, string keywordsFileName, AppDataBootstrapResult result)
        {
            string sourceCompiler = Path.Combine(sourceEditorPath, compilerFolderName);
            if (Directory.Exists(sourceCompiler))
            {
                int before = CountFiles(layout.CompilerPath);
                CopyDirectoryContents(sourceCompiler, layout.CompilerPath, result);
                if (CountFiles(layout.CompilerPath) > before) result.CompilerRestored = true;
            }

            string sourceKeywords = Path.Combine(sourceEditorPath, keywordsFileName);
            if (File.Exists(sourceKeywords) && !File.Exists(layout.KeywordsPath))
            {
                if (TryCopyFile(sourceKeywords, layout.KeywordsPath)) result.KeywordsRestored = true;
                else result.Warnings.Add("Could not copy " + sourceKeywords + " to " + layout.KeywordsPath + ".");
            }
        }

        private static void CopyDirectoryContents(string sourceDir, string destDir, AppDataBootstrapResult result)
        {
            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string dest = Path.Combine(destDir, Path.GetFileName(file));
                if (!TryCopyFile(file, dest))
                    result.Warnings.Add("Could not copy " + file + " to " + dest + ".");
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string destSub = Path.Combine(destDir, Path.GetFileName(dir));
                if (IsSamePath(dir, destSub)) continue;
                CopyDirectoryContents(dir, destSub, result);
            }
        }

        private static bool TryCopyFile(string source, string dest)
        {
            try
            {
                if (IsSamePath(source, dest)) return true;
                if (File.Exists(dest)) return true; // never overwrite files the user may have edited
                File.Copy(source, dest, false);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static int CountFiles(string path)
        {
            try { return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length; }
            catch (Exception) { return 0; }
        }

        private static bool IsSamePath(string first, string second)
        {
            if (String.IsNullOrEmpty(first) || String.IsNullOrEmpty(second)) return false;
            try
            {
                char[] separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
                string a = Path.GetFullPath(first).TrimEnd(separators);
                string b = Path.GetFullPath(second).TrimEnd(separators);
                bool windows = Environment.OSVersion.Platform == PlatformID.Win32NT ||
                               Environment.OSVersion.Platform == PlatformID.Win32Windows;
                return String.Equals(a, b, windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
