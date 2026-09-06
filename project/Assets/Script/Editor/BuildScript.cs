using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace EscapeProto.EditorTools
{
    /// <summary>
    /// ROM（配布用ビルド）を焼くビルドスクリプト。
    /// ・build_rom.bat から Unity のバッチモードで呼ばれる（-executeMethod EscapeProto.EditorTools.BuildScript.BuildWindows）
    /// ・エディタのメニュー Tools/EscapePrototype/Build/Windows ROM を焼く からも同じ処理を実行できる
    /// ・ビルド対象シーンは LoopPrototype（Build Settings の登録内容には依存しない）
    /// コマンドライン引数:
    ///   -buildPath &lt;dir&gt;   出力フォルダ（既定: リポジトリ直下の Build/Windows）
    ///   -dev                開発ビルド（Development Build + Script Debugging）
    ///   -version &lt;x.y.z&gt;   PlayerSettings.bundleVersion を上書き
    /// </summary>
    public static class BuildScript
    {
        private const string MainScene = "Assets/EscapePrototype/LoopPrototype.unity";
        private const string ProductName = "RENASCITA";

        [MenuItem("Tools/EscapePrototype/Build/Windows ROM を焼く")]
        public static void BuildWindowsFromMenu()
        {
            string dir = EditorUtility.SaveFolderPanel("ROMの出力フォルダ", DefaultBuildDir(), "Windows");
            if (string.IsNullOrEmpty(dir)) return;
            var report = Build(dir, false, null);
            if (report != null && report.summary.result == BuildResult.Succeeded)
                EditorUtility.RevealInFinder(report.summary.outputPath);
        }

        /// <summary>バッチモード用エントリ。失敗時は終了コード1でUnityを終了する</summary>
        public static void BuildWindows()
        {
            string buildPath = GetArg("-buildPath") ?? Path.Combine(DefaultBuildDir(), "Windows");
            bool dev = HasArg("-dev");
            string version = GetArg("-version");
            var report = Build(buildPath, dev, version);
            bool ok = report != null && report.summary.result == BuildResult.Succeeded;
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        private static BuildReport Build(string buildDir, bool development, string version)
        {
            if (!File.Exists(MainScene))
            {
                Debug.LogError($"[Build] メインシーンが見つかりません: {MainScene}");
                return null;
            }
            Directory.CreateDirectory(buildDir);
            string exe = Path.Combine(buildDir, ProductName + ".exe");

            PlayerSettings.productName = ProductName;
            if (!string.IsNullOrEmpty(version)) PlayerSettings.bundleVersion = version;

            var options = BuildOptions.None;
            if (development) options |= BuildOptions.Development | BuildOptions.AllowDebugging;

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { MainScene },
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = options,
            };

            Debug.Log($"[Build] 開始: {exe}  version={PlayerSettings.bundleVersion}  dev={development}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var report = BuildPipeline.BuildPlayer(opts);
            sw.Stop();

            var s = report.summary;
            string msg = $"[Build] {s.result}  size={s.totalSize / (1024 * 1024)}MB  errors={s.totalErrors}  warnings={s.totalWarnings}  time={sw.Elapsed.TotalSeconds:0}s\n  -> {s.outputPath}";
            if (s.result == BuildResult.Succeeded)
            {
                Debug.Log(msg);
                WriteBuildInfo(buildDir, development);
            }
            else
            {
                Debug.LogError(msg);
                foreach (var step in report.steps)
                    foreach (var m in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                        Debug.LogError($"  [{step.name}] {m.content}");
            }
            return report;
        }

        /// <summary>配布物に同梱するビルド情報（バージョン・日時・コミット）</summary>
        private static void WriteBuildInfo(string buildDir, bool development)
        {
            string commit = "unknown";
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    WorkingDirectory = RepoRoot(), RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    commit = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(3000);
                }
            }
            catch { /* gitが無い環境では unknown のまま */ }

            File.WriteAllText(Path.Combine(buildDir, "BUILD_INFO.txt"),
                $"{ProductName}\nversion: {PlayerSettings.bundleVersion}\nbuilt:   {DateTime.Now:yyyy-MM-dd HH:mm}\ncommit:  {commit}\nunity:   {Application.unityVersion}\ndev:     {development}\n");
        }

        private static string RepoRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        private static string DefaultBuildDir() => Path.Combine(RepoRoot(), "Build");

        private static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        private static bool HasArg(string name) =>
            Environment.GetCommandLineArgs().Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    }
}
