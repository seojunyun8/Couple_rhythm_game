using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Windows 빌드 실행 파일 옆에 런타임 .env를 자동 생성한다.
/// 저장소에 없는 프로젝트 루트 .env 또는 빌드 프로세스 환경변수만 사용한다.
/// </summary>
public sealed class BoothFirebaseBuildConfig : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    [Serializable]
    private sealed class ProjectConfig { public string firebaseProjectId; }

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.StandaloneWindows64)
            RuntimeLines(); // 설정이 없으면 오래 걸리는 빌드 전에 중단한다.
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64) return;
        string exePath = report.summary.outputPath;
        if (!exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new BuildFailedException("Windows 실행 파일 경로를 확인할 수 없습니다.");
        string directory = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(directory))
            throw new BuildFailedException("Windows 빌드 출력 폴더를 확인할 수 없습니다.");
        string destination = Path.Combine(directory, ".env");
        File.WriteAllLines(destination, RuntimeLines(), new UTF8Encoding(false));
        Debug.Log("[BoothBuild] 실행 파일 옆 .env에 Firebase 운영 설정을 넣었습니다. 값은 로그에 출력하지 않습니다.");
    }

    private static string[] RuntimeLines()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string configPath = Path.Combine(root, "Assets", "Resources", "FirebaseConfig.json");
        if (!File.Exists(configPath))
            throw new BuildFailedException("Assets/Resources/FirebaseConfig.json이 없습니다.");
        ProjectConfig config;
        try { config = JsonUtility.FromJson<ProjectConfig>(File.ReadAllText(configPath, Encoding.UTF8)); }
        catch (Exception) { throw new BuildFailedException("FirebaseConfig.json을 읽을 수 없습니다."); }
        if (config == null || string.IsNullOrWhiteSpace(config.firebaseProjectId))
            throw new BuildFailedException("FirebaseConfig.json의 프로젝트 ID가 비어 있습니다.");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string source = Path.Combine(root, ".env");
        if (File.Exists(source))
        {
            foreach (string raw in File.ReadAllLines(source, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int equals = line.IndexOf('=');
                if (equals < 1) continue;
                string name = line.Substring(0, equals).Trim();
                if (name != "FIREBASE_PROJECT_ID" && name != "FIREBASE_WEB_API_KEY" && name != "BANK_ACCOUNT_INFO")
                    continue;
                if (values.ContainsKey(name))
                    throw new BuildFailedException(".env에 중복된 설정 항목이 있습니다: " + name);
                values.Add(name, Clean(line.Substring(equals + 1)));
            }
        }

        string projectId = Get(values, "FIREBASE_PROJECT_ID");
        string apiKey = Get(values, "FIREBASE_WEB_API_KEY");
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(apiKey))
            throw new BuildFailedException("FIREBASE_PROJECT_ID와 FIREBASE_WEB_API_KEY를 프로젝트 루트 .env 또는 빌드 환경변수에 설정하세요.");
        if (projectId != config.firebaseProjectId.Trim())
            throw new BuildFailedException(".env의 Firebase 프로젝트 ID가 FirebaseConfig.json과 다릅니다.");

        var lines = new List<string> {
            "FIREBASE_PROJECT_ID=" + projectId,
            "FIREBASE_WEB_API_KEY=" + apiKey
        };
        string bankInfo = Get(values, "BANK_ACCOUNT_INFO");
        if (!string.IsNullOrWhiteSpace(bankInfo)) lines.Add("BANK_ACCOUNT_INFO=" + bankInfo);
        return lines.ToArray();
    }

    private static string Get(Dictionary<string, string> values, string name)
    {
        if (values.TryGetValue(name, out string value) && !string.IsNullOrWhiteSpace(value))
            return value;
        return Clean(Environment.GetEnvironmentVariable(name) ?? "");
    }

    private static string Clean(string value)
    {
        value = (value ?? "").Trim();
        if (value.Length >= 2 && ((value[0] == '"' && value[value.Length - 1] == '"') ||
                                  (value[0] == '\'' && value[value.Length - 1] == '\'')))
            value = value.Substring(1, value.Length - 2);
        if (value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            throw new BuildFailedException("빌드 환경 설정에 줄바꿈이 포함되어 있습니다.");
        return value;
    }
}
