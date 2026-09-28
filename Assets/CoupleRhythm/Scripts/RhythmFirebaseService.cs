using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CoupleRhythm
{
    public class RhythmFirebaseService : MonoBehaviour
    {
        private static RhythmFirebaseService instance;
        public static RhythmFirebaseService Instance
        {
            get
            {
                if (instance == null)
                {
                    GameObject go = new GameObject("RhythmFirebaseService");
                    instance = go.AddComponent<RhythmFirebaseService>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        [Header("Firebase Config")]
        public string firebaseProjectId = "your-firebase-project-id";
        private string BaseUrl => $"https://firestore.googleapis.com/v1/projects/{firebaseProjectId}/databases/(default)/documents";
        private string DocumentName(string path) => $"projects/{firebaseProjectId}/databases/(default)/documents/{path}";
        [Serializable] private class IntField { public string integerValue; }
        [Serializable] private class StatsFields { public IntField totalDolls, totalLegendaryDolls, totalPlays, totalRevenue, totalSuccesses; }
        [Serializable] private class StatsDocument { public string updateTime; public StatsFields fields; }
        [Serializable] private class StringField { public string stringValue; }
        [Serializable] private class ReceiptFields { public StringField kind, stockField; public IntField revenue; }
        [Serializable] private class ReceiptDocument { public ReceiptFields fields; }
        [Serializable] private class StringWrapper { public string value; }
        private const int PrizeDollQuantity = 2;
        private static string Quoted(string value) => JsonUtility.ToJson(new StringWrapper { value = value ?? "" }).Substring(9).TrimEnd('}');
        private static string Number(string name, int value) => $"\"{name}\":{{\"integerValue\":\"{value}\"}}";

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            LoadConfig();
        }

        private void LoadConfig()
        {
            string envPid = EnvLoader.Get("FIREBASE_PROJECT_ID");
            if (!string.IsNullOrEmpty(envPid) && envPid != "your-firebase-project-id")
            {
                firebaseProjectId = envPid;
                Debug.Log($"[RhythmFirebase] ✅ .env 에서 Firebase Project ID 로드: {firebaseProjectId}");
                return;
            }

            TextAsset configAsset = Resources.Load<TextAsset>("FirebaseConfig");
            if (configAsset != null)
            {
                try
                {
                    var parsed = JsonUtility.FromJson<FirebaseConfigData>(configAsset.text);
                    if (parsed != null && !string.IsNullOrEmpty(parsed.firebaseProjectId) && parsed.firebaseProjectId != "your-firebase-project-id")
                    {
                        firebaseProjectId = parsed.firebaseProjectId;
                        Debug.Log($"[RhythmFirebase] ✅ Resources/FirebaseConfig 에서 Project ID 로드: {firebaseProjectId}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RhythmFirebase] FirebaseConfig 로드 실패: {ex.Message}");
                }
            }
        }

        [Serializable]
        private class FirebaseConfigData
        {
            public string firebaseProjectId;
        }

        /// <summary>
        /// 결제 완료 또는 게임 시작 시 부스 전체 플레이 수와 매출을 증가시킵니다.
        /// </summary>
        public void RecordGameStart(string roundId, int revenueAmount, Action<bool, string> callback)
        {
            StartCoroutine(ChangeStats(roundId, "start", "", revenueAmount, callback));
        }

        public void RedeemPrize(string roundId, bool legendary, Action<bool, string> callback)
        {
            StartCoroutine(ChangeStats(roundId, "prize", legendary ? "totalLegendaryDolls" : "totalDolls", 0, callback));
        }

        /// <summary>
        /// QR 결제 화면에 표시할 현재 일반/레전드 인형 재고를 조회합니다.
        /// </summary>
        public void GetPrizeInventory(Action<bool, int, int, string> callback)
        {
            StartCoroutine(GetPrizeInventoryRoutine(callback));
        }

        private IEnumerator GetPrizeInventoryRoutine(Action<bool, int, int, string> callback)
        {
            if (string.IsNullOrEmpty(firebaseProjectId) || firebaseProjectId == "your-firebase-project-id")
            {
                callback?.Invoke(false, 0, 0, "Firebase 설정을 확인해 주세요.");
                yield break;
            }

            long code = 0;
            string body = null;
            yield return Send("GET", "/GameState/stats", null, (status, response) =>
            {
                code = status;
                body = response;
            });

            StatsDocument doc;
            try { doc = code == 200 ? JsonUtility.FromJson<StatsDocument>(body) : null; }
            catch { doc = null; }

            if (doc?.fields != null &&
                Read(doc.fields.totalDolls, out int dolls) &&
                Read(doc.fields.totalLegendaryDolls, out int legendaryDolls))
            {
                callback?.Invoke(true, dolls, legendaryDolls, null);
                yield break;
            }

            string error = code == 401 ? "스태프 로그인이 필요합니다." :
                code == 200 ? "재고 필드를 확인해 주세요." : "재고 조회 실패: " + code;
            callback?.Invoke(false, 0, 0, error);
        }

        private IEnumerator ChangeStats(string roundId, string kind, string stockField, int revenue, Action<bool, string> callback)
        {
            if (string.IsNullOrEmpty(roundId) || firebaseProjectId == "your-firebase-project-id")
            { callback?.Invoke(false, "Firebase 설정을 확인해 주세요."); yield break; }
            string receipt = kind == "start" ? "GameRounds/rhythm_" + roundId : "InventoryChanges/rhythm_" + roundId;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                long code = 0; string body = null;
                yield return Send("GET", "/" + receipt, null, (c, b) => { code = c; body = b; });
                if (code == 200)
                {
                    // The receipt resolves a lost response without applying effects twice.
                    ReceiptDocument saved = null;
                    try { saved = JsonUtility.FromJson<ReceiptDocument>(body); } catch { }
                    bool same = saved?.fields?.kind?.stringValue == "rhythm_" + kind &&
                        saved.fields.stockField?.stringValue == stockField &&
                        saved.fields.revenue?.integerValue == revenue.ToString();
                    callback?.Invoke(same, same ? null : "같은 게임 번호에 다른 지급 기록이 있습니다.");
                    yield break;
                }
                if (code != 404) { callback?.Invoke(false, "저장 확인 실패: " + code); yield break; }
                yield return Send("GET", "/GameState/stats", null, (c, b) => { code = c; body = b; });
                if (code != 200) { callback?.Invoke(false, "운영 통계 조회 실패: " + code); yield break; }
                StatsDocument doc;
                try { doc = JsonUtility.FromJson<StatsDocument>(body); }
                catch { doc = null; }
                if (doc?.fields == null || string.IsNullOrEmpty(doc.updateTime) ||
                    !Read(doc.fields.totalDolls, out int dolls) || !Read(doc.fields.totalLegendaryDolls, out int legends) ||
                    !Read(doc.fields.totalPlays, out int plays) || !Read(doc.fields.totalRevenue, out int totalRevenue) ||
                    !Read(doc.fields.totalSuccesses, out int successes))
                { callback?.Invoke(false, "운영 통계 필드를 확인해 주세요."); yield break; }
                if (kind == "prize" && ((stockField == "totalDolls" && dolls < PrizeDollQuantity) ||
                    (stockField == "totalLegendaryDolls" && legends < PrizeDollQuantity)))
                { callback?.Invoke(false, "재고가 2개 미만입니다. 지급을 보류하고 운영진에게 알려 주세요."); yield break; }
                if ((kind == "start" && (plays == int.MaxValue || revenue < 0 || totalRevenue > int.MaxValue - revenue)) ||
                    (kind == "prize" && successes == int.MaxValue))
                { callback?.Invoke(false, "운영 통계 범위를 확인해 주세요."); yield break; }
                string statsFields = kind == "start" ? Number("totalPlays", plays + 1) + "," +
                    Number("totalRevenue", totalRevenue + revenue) : Number(stockField,
                    (stockField == "totalDolls" ? dolls : legends) - PrizeDollQuantity) + "," + Number("totalSuccesses", successes + 1);
                string mask = kind == "start" ? "\"totalPlays\",\"totalRevenue\"" :
                    "\"" + stockField + "\",\"totalSuccesses\"";
                string receiptFields = "\"kind\":{\"stringValue\":\"rhythm_" + kind + "\"}," +
                    "\"stockField\":{\"stringValue\":\"" + stockField + "\"}," + Number("revenue", revenue);
                string payload = "{\"writes\":[{\"update\":{\"name\":\"" + DocumentName(receipt) +
                    "\",\"fields\":{" + receiptFields + "}},\"currentDocument\":{\"exists\":false}},{\"update\":{\"name\":\"" +
                    DocumentName("GameState/stats") + "\",\"fields\":{" + statsFields + "}},\"updateMask\":{\"fieldPaths\":[" +
                    mask + "]},\"currentDocument\":{\"updateTime\":\"" + doc.updateTime + "\"}}]}";
                yield return Send("POST", ":commit", payload, (c, b) => { code = c; body = b; });
                if (code == 200) { callback?.Invoke(true, null); yield break; }
                if (code != 0 && code != 409 && code != 412 && code != 503)
                { callback?.Invoke(false, "저장 실패: " + code); yield break; }
            }
            callback?.Invoke(false, "저장 상태 확인 필요: 같은 게임 번호로 다시 시도해 주세요.");
        }

        private static bool Read(IntField field, out int value) =>
            int.TryParse(field?.integerValue, out value) && value >= 0;

        private IEnumerator Send(string method, string path, string body, Action<long, string> callback)
        {
            string token = null;
            yield return BoothStaffAuth.Instance.EnsureIdToken(value => token = value);
            if (string.IsNullOrEmpty(token)) { callback?.Invoke(401, null); yield break; }
            using (var request = new UnityWebRequest(BaseUrl + path, method))
            {
                request.timeout = 15;
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", "Bearer " + token);
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                yield return request.SendWebRequest();
                callback?.Invoke(request.responseCode, request.downloadHandler.text);
            }
        }

        /// <summary>
        /// 곡 종료 시 커플의 성적을 실시간 랭킹 컬렉션(RhythmLeaderboard)에 저장합니다.
        /// </summary>
        public void SubmitScore(string roundId, string songId, string songTitle, float accuracy, int maxCombo, int perfect, int good, int miss, int wrongPress)
        {
            if (string.IsNullOrEmpty(firebaseProjectId) || firebaseProjectId == "your-firebase-project-id") return;
            StartCoroutine(SubmitScoreRoutine(roundId, songId, songTitle, accuracy, maxCombo, perfect, good, miss, wrongPress));
        }

        private IEnumerator SubmitScoreRoutine(string roundId, string songId, string songTitle, float accuracy, int maxCombo, int perfect, int good, int miss, int wrongPress)
        {
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            string jsonPayload = "{\"fields\":{" +
                $"\"songId\":{{\"stringValue\":{Quoted(songId)}}}," +
                $"\"songTitle\":{{\"stringValue\":{Quoted(songTitle)}}}," +
                $"\"accuracy\":{{\"doubleValue\":{accuracy.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}}}," +
                $"\"maxCombo\":{{\"integerValue\":\"{maxCombo}\"}}," +
                $"\"perfectCount\":{{\"integerValue\":\"{perfect}\"}}," +
                $"\"goodCount\":{{\"integerValue\":\"{good}\"}}," +
                $"\"missCount\":{{\"integerValue\":\"{miss}\"}}," +
                $"\"wrongPressCount\":{{\"integerValue\":\"{wrongPress}\"}}," +
                $"\"timestamp\":{{\"stringValue\":\"{timestamp}\"}}" +
            "}}";

            string payload = "{\"writes\":[{\"update\":{\"name\":\"" +
                DocumentName("RhythmLeaderboard/" + roundId) + "\"," + jsonPayload.Substring(1) +
                ",\"currentDocument\":{\"exists\":false}}]}";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                long code = 0;
                yield return Send("POST", ":commit", payload, (status, _) => code = status);
                if (code == 200 || code == 409) yield break;
                if (code != 0 && code != 503) break;
                yield return new WaitForSecondsRealtime(1f);
            }
            Debug.LogWarning("[RhythmFirebase] 랭킹 저장 확인 필요: " + roundId);
        }
    }
}
