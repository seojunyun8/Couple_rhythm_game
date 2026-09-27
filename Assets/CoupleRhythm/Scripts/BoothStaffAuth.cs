using System;
using System.Collections;
using System.Text;
using CoupleRhythm;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

// Staff sign-in is required before this kiosk can read participant profiles or
// write results. Tokens stay in memory; neither passwords nor refresh tokens
// are written to PlayerPrefs, Resources, or the project directory.
public sealed class BoothStaffAuth : MonoBehaviour
{
    public static BoothStaffAuth Instance { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(idToken);
    public bool IsAdmin => IsAuthenticated && HasAdminClaim(idToken);
    public bool IsConfigured => !string.IsNullOrWhiteSpace(webApiKey) &&
        RhythmFirebaseService.Instance != null && RhythmFirebaseService.Instance.firebaseProjectId != "your-firebase-project-id";

    private string webApiKey;
    private string idToken;
    private string refreshToken;
    private float tokenExpiresAt;
    private string email = "";
    private string password = "";
    private string status = "";
    private bool signingIn;
    private bool refreshing;

    [Serializable] private sealed class Config { public string firebaseWebApiKey; }
    [Serializable] private sealed class SignInRequest
    {
        public string email;
        public string password;
        public bool returnSecureToken = true;
    }
    [Serializable] private sealed class SignInReply
    {
        public string idToken;
        public string refreshToken;
        public string expiresIn;
    }
    [Serializable] private sealed class AuthErrorReply { public AuthError error; }
    [Serializable] private sealed class AuthError { public string message; }
    [Serializable] private sealed class RefreshReply
    {
        public string id_token;
        public string refresh_token;
        public string expires_in;
    }
    [Serializable] private sealed class TokenClaims
    {
        public string aud;
        public bool boothStaff;
        public bool boothAdmin;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        GameObject host = new GameObject(nameof(BoothStaffAuth));
        DontDestroyOnLoad(host);
        host.AddComponent<BoothStaffAuth>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        webApiKey = EnvLoader.Get("FIREBASE_WEB_API_KEY");
        if (string.IsNullOrWhiteSpace(webApiKey))
        {
            TextAsset config = Resources.Load<TextAsset>("FirebaseConfig");
            if (config != null)
            {
                try { webApiKey = JsonUtility.FromJson<Config>(config.text)?.firebaseWebApiKey; }
                catch (Exception) { webApiKey = ""; }
            }
        }
        webApiKey = (webApiKey ?? "").Trim();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnGUI()
    {
        if (IsAuthenticated ||
            false) return;

        float width = Mathf.Min(450f, Screen.width - 20f);
        float left = (Screen.width - width) * 0.5f;
        float top = (Screen.height - 255f) * 0.5f;
        GUI.Box(new Rect(left, top, width, 255f), "스태프 로그인");
        GUI.Label(new Rect(left + 20f, top + 35f, width - 40f, 25f), "운영 전에 Firebase 스태프 계정으로 로그인해 주세요.");
        email = GUI.TextField(new Rect(left + 20f, top + 70f, width - 40f, 27f), email);
        password = GUI.PasswordField(new Rect(left + 20f, top + 103f, width - 40f, 27f), password, '*');
        GUI.enabled = !signingIn;
        if (GUI.Button(new Rect(left + 20f, top + 139f, width - 40f, 30f), signingIn ? "로그인 중..." : "로그인"))
            BeginSignIn();
        GUI.enabled = true;
        GUI.Label(new Rect(left + 20f, top + 177f, width - 40f, 67f), status);
    }

    private void BeginSignIn()
    {
        if (signingIn) return;
        if (string.IsNullOrWhiteSpace(webApiKey))
        { status = "Firebase Web API 키가 없습니다. 최신 빌드 설정을 확인해 주세요."; return; }
        if (!IsConfigured) { status = "Firebase 프로젝트 ID를 읽지 못했습니다."; return; }
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        { status = "이메일과 비밀번호를 입력해 주세요."; return; }
        string enteredPassword = password;
        password = "";
        StartCoroutine(SignInRoutine(email.Trim(), enteredPassword));
    }

    private IEnumerator SignInRoutine(string enteredEmail, string enteredPassword)
    {
        signingIn = true;
        status = "스태프 권한 확인 중...";
        string url = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=" +
            Uri.EscapeDataString(webApiKey);
        string body = JsonUtility.ToJson(new SignInRequest
        {
            email = enteredEmail, password = enteredPassword, returnSecureToken = true
        });
        using (var request = new UnityWebRequest(url, "POST"))
        {
            request.timeout = 15;
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();
            string response = request.downloadHandler.text;
            SignInReply reply = request.responseCode == 200 ? Parse<SignInReply>(response) : null;
            if (request.responseCode != 200 || reply == null || !HasStaffClaim(reply.idToken))
            {
                status = request.result == UnityWebRequest.Result.ConnectionError ?
                    "네트워크 연결을 확인한 뒤 다시 로그인해 주세요." :
                    request.responseCode == 200 ?
                        reply == null || string.IsNullOrEmpty(reply.idToken) ?
                            "Firebase 로그인 응답이 올바르지 않습니다." :
                            "이 계정에는 부스 스태프 권한이 없습니다. 다시 로그인해 주세요." :
                        SignInError(request.responseCode, response);
                SignOut();
            }
            else
            {
                SetTokens(reply.idToken, reply.refreshToken, reply.expiresIn);
                status = "";
            }
        }
        signingIn = false;
    }

    public IEnumerator EnsureIdToken(Action<string> callback)
    {
        while (signingIn || refreshing) yield return null;
        if (!IsAuthenticated) { callback?.Invoke(null); yield break; }
        if (Time.realtimeSinceStartup >= tokenExpiresAt)
        {
            refreshing = true;
            yield return RefreshTokenRoutine();
            refreshing = false;
        }
        callback?.Invoke(idToken);
    }

    private IEnumerator RefreshTokenRoutine()
    {
        if (string.IsNullOrEmpty(refreshToken)) { SignOut(); yield break; }
        string url = "https://securetoken.googleapis.com/v1/token?key=" + Uri.EscapeDataString(webApiKey);
        string body = "grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(refreshToken);
        using (var request = new UnityWebRequest(url, "POST"))
        {
            request.timeout = 15;
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
            yield return request.SendWebRequest();
            RefreshReply reply = Parse<RefreshReply>(request.downloadHandler.text);
            if (request.responseCode != 200 || reply == null || !HasStaffClaim(reply.id_token))
            {
                SignOut();
                status = "세션이 만료됐습니다. 스태프가 다시 로그인해 주세요.";
                yield break;
            }
            SetTokens(reply.id_token, reply.refresh_token, reply.expires_in);
        }
    }

    private void SetTokens(string token, string nextRefreshToken, string expiresIn)
    {
        idToken = token;
        refreshToken = nextRefreshToken;
        if (!int.TryParse(expiresIn, out int seconds)) seconds = 3600;
        tokenExpiresAt = Time.realtimeSinceStartup + Mathf.Max(0, seconds - 60);
    }

    private bool HasStaffClaim(string token)
    {
        try
        {
            string[] parts = token.Split('.');
            if (parts.Length != 3) return false;
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            TokenClaims claims = JsonUtility.FromJson<TokenClaims>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return claims != null && claims.boothStaff && claims.aud == RhythmFirebaseService.Instance.firebaseProjectId;
        }
        catch (Exception) { return false; }
    }

    private bool HasAdminClaim(string token)
    {
        try
        {
            string[] parts = token.Split('.');
            if (parts.Length != 3) return false;
            string payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            var claims = JsonUtility.FromJson<TokenClaims>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return claims != null && claims.boothStaff && claims.boothAdmin &&
                claims.aud == RhythmFirebaseService.Instance.firebaseProjectId;
        }
        catch (Exception) { return false; }
    }

    public void SignOut()
    {
        idToken = null;
        refreshToken = null;
        tokenExpiresAt = 0f;
    }

    private static T Parse<T>(string json) where T : class
    {
        try { return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<T>(json); }
        catch (Exception) { return null; }
    }

    // Firebase Auth REST errors carry the useful cause in error.message.
    // Never display or log the full response, request body, API key, or password.
    private static string SignInError(long httpCode, string response)
    {
        string code = Parse<AuthErrorReply>(response)?.error?.message ?? "";
        int separator = code.IndexOf(':');
        if (separator >= 0) code = code.Substring(0, separator);
        code = code.Trim();
        switch (code)
        {
            case "INVALID_LOGIN_CREDENTIALS":
            case "INVALID_CREDENTIAL":
            case "INVALID_PASSWORD":
            case "EMAIL_NOT_FOUND":
                return "이메일 또는 비밀번호가 맞지 않습니다. 스태프 계정을 확인해 주세요.";
            case "INVALID_EMAIL":
                return "이메일 주소 형식을 확인해 주세요.";
            case "USER_DISABLED":
                return "사용 중지된 계정입니다. Firebase Authentication 사용자를 확인해 주세요.";
            case "OPERATION_NOT_ALLOWED":
            case "PASSWORD_LOGIN_DISABLED":
                return "Firebase 이메일/비밀번호 로그인 제공자가 꺼져 있습니다.";
            case "API_KEY_INVALID":
            case "INVALID_API_KEY":
            case "API_KEY_SERVICE_BLOCKED":
            case "PROJECT_NOT_FOUND":
                return "Firebase Web API 키 또는 프로젝트 설정을 확인해 주세요. (" + code + ")";
            case "TOO_MANY_ATTEMPTS_TRY_LATER":
                return "로그인 시도가 너무 많습니다. 잠시 후 다시 시도해 주세요.";
        }
        if (code.StartsWith("API key not valid", StringComparison.OrdinalIgnoreCase))
            return "Firebase Web API 키를 확인해 주세요.";
        // Show only a short machine-readable error code, not arbitrary server text.
        if (code.Length > 0 && code.Length <= 64)
        {
            bool safe = true;
            foreach (char c in code)
                if (!(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_') { safe = false; break; }
            if (safe) return "Firebase 로그인 오류: " + code + " (HTTP " + httpCode + ")";
        }
        return "Firebase 로그인에 실패했습니다. (HTTP " + httpCode + ")";
    }
}
