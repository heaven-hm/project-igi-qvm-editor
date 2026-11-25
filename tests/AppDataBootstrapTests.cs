using System;
using System.IO;

namespace QVM_Editor.Tests
{
    // Console test runner: compiles AppDataBootstrap.cs into the same assembly so the
    // internal types can be exercised without WinForms, Newtonsoft.Json or Scintilla.
    internal static class AppDataBootstrapTests
    {
        private static int passed;
        private static int failed;

        private static void Check(bool condition, string name, string detail = null)
        {
            if (condition)
            {
                passed++;
                Console.WriteLine("PASS " + name);
            }
            else
            {
                failed++;
                Console.WriteLine("FAIL " + name + (String.IsNullOrEmpty(detail) ? "" : " :: " + detail));
            }
        }

        private static void Run(string name, Action<string> test)
        {
            string root = Path.Combine(Path.GetTempPath(), "qvm-appdata-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                test(root);
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("FAIL " + name + " threw :: " + ex);
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch (Exception) { /* best effort cleanup */ }
            }
        }

        private static string MakeBundle(string root)
        {
            string editor = Path.Combine(root, "bundle", "QEditor");
            string compile = Path.Combine(editor, "QCompiler", "Compile");
            Directory.CreateDirectory(compile);
            File.WriteAllText(Path.Combine(compile, "compile_v5.bat"), "compile");
            File.WriteAllText(Path.Combine(editor, "keywords.txt"), "Task_New Building");
            return editor;
        }

        private static void PathLayoutUsesPlatformSeparators(string root)
        {
            string appData = Path.Combine(root, "AppData");
            var layout = new AppDataLayout(appData, "QEditor", "QCompiler", "keywords.txt");
            Check(layout.EditorPath == Path.Combine(appData, "QEditor"), "layout editor path", layout.EditorPath);
            Check(layout.CompilerPath == Path.Combine(appData, "QEditor", "QCompiler"), "layout compiler path", layout.CompilerPath);
            if (Path.DirectorySeparatorChar == '/')
                Check(!layout.CompilerPath.Contains("\\"), "no Windows separator on Unix", layout.CompilerPath);
        }

        private static void MigratesBundleIntoEmptyAppData(string root)
        {
            string source = MakeBundle(root);
            string appData = Path.Combine(root, "AppData");
            Directory.CreateDirectory(appData);

            var result = AppDataBootstrap.Ensure(appData, new[] { source });
            string compilerFile = Path.Combine(appData, "QEditor", "QCompiler", "Compile", "compile_v5.bat");

            Check(result.Success, "fresh install succeeds", result.ErrorMessage);
            Check(Directory.Exists(Path.Combine(appData, "QEditor", "QCompiler")), "QCompiler created");
            Check(File.Exists(compilerFile), "compiler file copied");
            Check(File.Exists(Path.Combine(appData, "QEditor", "keywords.txt")), "keywords copied");
            Check(File.Exists(Path.Combine(source, "QCompiler", "Compile", "compile_v5.bat")), "source not deleted");
            Check(result.CompilerRestored && !result.CompilerFilesMissing, "compiler restored flag");
        }

        private static void RestoresMissingQCompilerWhenEditorAlreadyExists(string root)
        {
            // Exact issue #4 shape: %APPDATA%\QEditor exists (manual copy) but has no QCompiler.
            string source = MakeBundle(root);
            string appData = Path.Combine(root, "AppData");
            string editor = Path.Combine(appData, "QEditor");
            Directory.CreateDirectory(editor);
            File.WriteAllText(Path.Combine(editor, "keywords.txt"), "user keywords");

            var result = AppDataBootstrap.Ensure(appData, new[] { source });

            Check(result.Success, "partial install succeeds", result.ErrorMessage);
            Check(Directory.Exists(Path.Combine(editor, "QCompiler")), "missing QCompiler restored");
            Check(File.Exists(Path.Combine(editor, "QCompiler", "Compile", "compile_v5.bat")), "compiler files restored");
            Check(File.ReadAllText(Path.Combine(editor, "keywords.txt")) == "user keywords", "existing keywords not overwritten");
            Check(!result.CompilerFilesMissing, "compiler files present");
        }

        private static void MergesPartialCompilerDirectory(string root)
        {
            string source = MakeBundle(root);
            string appData = Path.Combine(root, "AppData");
            string compiler = Path.Combine(appData, "QEditor", "QCompiler");
            Directory.CreateDirectory(compiler);
            File.WriteAllText(Path.Combine(compiler, "existing.txt"), "keep");

            var result = AppDataBootstrap.Ensure(appData, new[] { source });

            Check(result.Success, "partial compiler succeeds", result.ErrorMessage);
            Check(File.Exists(Path.Combine(compiler, "existing.txt")), "existing file kept");
            Check(File.Exists(Path.Combine(compiler, "Compile", "compile_v5.bat")), "missing subtree merged");
        }

        private static void CreatesSkeletonWhenNoBundleExists(string root)
        {
            string appData = Path.Combine(root, "AppData");
            Directory.CreateDirectory(appData);
            string missingSource = Path.Combine(root, "nowhere", "QEditor");

            var result = AppDataBootstrap.Ensure(appData, new[] { missingSource });

            Check(result.Success, "no bundle is not fatal", result.ErrorMessage);
            Check(Directory.Exists(Path.Combine(appData, "QEditor", "QCompiler")), "skeleton directories created");
            Check(result.CompilerFilesMissing, "compiler files flagged missing");
        }

        private static void RejectsEmptyAppDataPath(string root)
        {
            var nullResult = AppDataBootstrap.Ensure(null, new string[0]);
            var blankResult = AppDataBootstrap.Ensure("   ", new string[0]);
            Check(!nullResult.Success && nullResult.ErrorMessage.Contains("APPDATA"), "null AppData rejected with guidance", nullResult.ErrorMessage);
            Check(!blankResult.Success && !String.IsNullOrEmpty(blankResult.ErrorPath), "blank AppData rejected with path", blankResult.ErrorMessage);
        }

        private static void ReportsUncreatableAppDataPath(string root)
        {
            string blocked = Path.Combine(root, "not-a-directory");
            File.WriteAllText(blocked, "file");

            var result = AppDataBootstrap.Ensure(blocked, new string[0]);

            Check(!result.Success, "file in place of AppData fails", result.ErrorMessage);
            Check(!String.IsNullOrEmpty(result.ErrorPath) && !String.IsNullOrEmpty(result.ErrorMessage),
                "failure reports path and reason", (result.ErrorPath ?? "") + " / " + (result.ErrorMessage ?? ""));
        }

        private static void SelfSourceDoesNotRecurse(string root)
        {
            string appData = Path.Combine(root, "AppData");
            string editor = Path.Combine(appData, "QEditor");
            string compiler = Path.Combine(editor, "QCompiler");
            Directory.CreateDirectory(compiler);
            File.WriteAllText(Path.Combine(compiler, "only.txt"), "one");

            var result = AppDataBootstrap.Ensure(appData, new[] { editor });

            Check(result.Success, "source equal to destination succeeds", result.ErrorMessage);
            Check(Directory.GetFiles(compiler, "*", SearchOption.AllDirectories).Length == 1, "no recursive copy");
        }

        public static int Main()
        {
            Console.WriteLine("AppDataBootstrap tests (host: " + Environment.OSVersion.Platform + ")");
            Run("layout", PathLayoutUsesPlatformSeparators);
            Run("fresh migration", MigratesBundleIntoEmptyAppData);
            Run("issue #4 partial install", RestoresMissingQCompilerWhenEditorAlreadyExists);
            Run("partial compiler merge", MergesPartialCompilerDirectory);
            Run("no bundle fallback", CreatesSkeletonWhenNoBundleExists);
            Run("empty AppData", RejectsEmptyAppDataPath);
            Run("uncreatable AppData", ReportsUncreatableAppDataPath);
            Run("self source", SelfSourceDoesNotRecurse);
            Console.WriteLine("TOTAL: " + passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }
    }
}
