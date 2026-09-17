using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CoupleRhythm
{
    public sealed class CoupleRhythmGame : MonoBehaviour
    {
        private enum GameState
        {
            Payment,
            SongSelect,
            Countdown,
            Playing,
            Results
        }

        private readonly struct ChartNote
        {
            public readonly HeartKind Kind;
            public readonly float TargetTime;
            public readonly float HoldDuration;

            public ChartNote(HeartKind kind, float targetTime, float holdDuration)
            {
                Kind = kind;
                TargetTime = targetTime;
                HoldDuration = holdDuration;
            }
        }

        private readonly struct RhythmTemplate
        {
            public readonly float[] BeatOffsets;
            public readonly int BurstStart;
            public readonly int BurstLength;
            public readonly int MinimumDensity;

            public RhythmTemplate(float[] beatOffsets, int burstStart, int burstLength, int minimumDensity)
            {
                BeatOffsets = beatOffsets;
                BurstStart = burstStart;
                BurstLength = burstLength;
                MinimumDensity = minimumDensity;
            }

            public bool IsBurstNote(int index)
            {
                return index >= BurstStart && index < BurstStart + BurstLength;
            }
        }


        // Four-beat rhythm shapes are randomly recombined each round. Their offsets stay
        // on the beat grid, so the chart varies without drifting away from the song BPM.
        private static readonly RhythmTemplate[] RhythmTemplates =
        {
            new RhythmTemplate(new[] { 0f, 1f }, -1, 0, 1),
            new RhythmTemplate(new[] { 0f, 2f }, -1, 0, 1),
            new RhythmTemplate(new[] { 0f, 1f, 2f, 3f }, -1, 0, 1),
            new RhythmTemplate(new[] { 0f, 0.5f, 1f, 2.5f }, 0, 3, 1),
            new RhythmTemplate(new[] { 0f, 0.5f, 1f, 1.5f, 3f }, 0, 4, 1),
            new RhythmTemplate(new[] { 0f, 1.5f, 2f, 2.5f, 3f }, 1, 4, 1),
            new RhythmTemplate(new[] { 0f, 0.5f, 1f, 2f, 2.5f, 3f, 3.5f }, 3, 4, 2),
            new RhythmTemplate(new[] { 0f, 0.25f, 0.5f, 0.75f, 2f, 3f }, 0, 4, 3),
            new RhythmTemplate(new[] { 0f, 0.75f, 1.5f, 2.25f, 3f, 3.5f }, -1, 0, 4),
            new RhythmTemplate(new[] { 0f, 0.25f, 0.5f, 0.75f, 1.25f, 1.5f, 2f, 2.25f, 2.5f, 3f, 3.5f }, 0, 4, 4),
            new RhythmTemplate(new[] { 0f, 0.5f, 1f, 1.25f, 1.5f, 1.75f, 2.5f, 3f, 3.25f, 3.5f, 3.75f }, 2, 4, 4),
            new RhythmTemplate(new[] { 0f, 0.25f, 0.5f, 0.75f, 1f, 1.5f, 2f, 2.5f, 2.75f, 3f, 3.25f, 3.5f }, 0, 4, 4)
        };

        private static CoupleRhythmGame instance;
        private readonly List<HeartNoteView> notes = new List<HeartNoteView>();
        private readonly List<ChartNote> chartNotes = new List<ChartNote>();
        private readonly Dictionary<int, AudioClip> demoClips = new Dictionary<int, AudioClip>();
        private readonly List<Slider> adminSliders = new List<Slider>();

        private readonly Color blue = new Color(0.25f, 0.68f, 1f, 1f);
        private readonly Color red = new Color(1f, 0.29f, 0.43f, 1f);
        private readonly Color purple = new Color(0.72f, 0.39f, 1f, 1f);
        private readonly Color ink = new Color(0.29f, 0.15f, 0.31f, 1f);
        private readonly Color cream = new Color(1f, 0.97f, 0.96f, 1f);

        private SongDefinition[] songs;
        private RhythmSettings settings;
        private AudioSource musicSource;
        private Canvas canvas;
        private RectTransform paymentRoot;
        private RectTransform selectionRoot;
        private RectTransform gameRoot;
        private RectTransform countdownRoot;
        private RectTransform resultRoot;
        private RectTransform adminRoot;
        private RectTransform noteRoot;
        private Text countdownText;
        private Text accuracyText;
        private Text comboText;
        private Text judgementText;
        private Text songHudText;
        private Text resultAccuracyText;
        private Text resultStatsText;
        private Text resultRewardText;
        private RectTransform fireworksRoot;
        private Text celebrationText;
        private Text adminHelpText;
        private Image progressFill;
        private readonly Image[] targets = new Image[3];

        private GameState state;
        private int selectedSongIndex;
        private float currentSongBpm;
        private int nextNoteIndex;
        private int chartGeneration;
        private float songDuration;
        private float clipPlaybackStart;
        private float clipPlaybackEnd;
        private bool usingDemoClip;
        private int combo;
        private int maxCombo;
        private int perfectCount;
        private int goodCount;
        private int missCount;
        private int wrongPressCount;
        private float earnedAccuracyWeight;
        private float judgedAccuracyWeight;
        private float maximumAccuracyWeight;
        private bool adminOpen;
        private bool resumeMusicAfterAdmin;
        private float playbackStartRealtime;
        private Coroutine judgementRoutine;

        private const float TravelTime = 2.15f;
        private const float SpawnY = 520f;
        private const float TargetY = -327f;
        private const float GoodAccuracyRatio = 0.60f;
        private const float WrongPressAccuracyWeight = 1f;
        private const float PrizeAccuracyThreshold = 90f;
        private const float PremiumPrizeAccuracyThreshold = 100f;
        private static readonly float[] LaneX = { -365f, 0f, 365f };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<CoupleRhythmGame>() != null)
                return;
            GameObject host = new GameObject("Two Hearts One Beat");
            host.AddComponent<CoupleRhythmGame>();
            DontDestroyOnLoad(host);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;

            songs = CreateSongLibrary();
            settings = RhythmSettings.Load(songs);
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = false;
            musicSource.volume = 0.85f;

            BuildInterface();
            EnsureEventSystem();
            ShowPayment();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (adminOpen)
                return;

            if (state == GameState.Playing)
            {
                if (keyboard != null)
                {
                    bool playerOne = keyboard.aKey.wasPressedThisFrame;
                    bool playerTwo = keyboard.lKey.wasPressedThisFrame;
                    if (playerOne)
                        TryHit(1);
                    if (playerTwo)
                        TryHit(2);
                    if (keyboard.escapeKey.wasPressedThisFrame)
                    {
                        ShowSongSelect();
                        return;
                    }
                }

                float songTime = CurrentSongTime;
                SpawnUpcomingNotes(songTime);
                UpdateNotes(songTime);
                UpdateHud(songTime);

                UpdateHighlightFade();
                if (musicSource.clip != null && musicSource.isPlaying && CurrentAbsoluteAudioTime >= clipPlaybackEnd - 0.02f)
                {
                    musicSource.Stop();
                    FinishSong();
                }
                else if (musicSource.clip != null && !musicSource.isPlaying && Time.unscaledTime - playbackStartRealtime > 1f)
                    FinishSong();
            }
        }

        private float CurrentSongTime
        {
            get
            {
                if (musicSource.clip == null)
                    return 0f;
                float audioTime = musicSource.timeSamples > 0 ? musicSource.timeSamples / (float)musicSource.clip.frequency : musicSource.time;
                return audioTime - clipPlaybackStart + settings.AudioOffset;
            }
        }

        private float CurrentAbsoluteAudioTime
        {
            get
            {
                if (musicSource.clip == null)
                    return 0f;
                return musicSource.timeSamples > 0
                    ? musicSource.timeSamples / (float)musicSource.clip.frequency
                    : musicSource.time;
            }
        }

        private SongDefinition[] CreateSongLibrary()
        {
            return new[]
            {
                // Beat offsets were measured from the decoded AudioClip transients. They
                // anchor each generated chart to the master recording instead of time 0.
                new SongDefinition("redred", "REDRED", "CORTIS", "redred", 121f, 0.2845f, 40.0f, 62.0f, new Color(1f, 0.31f, 0.42f)),
                new SongDefinition("its_me", "it's me", "ILLIT · 아일릿", "its_me", 147f, 0.1640f, 32.0f, 59.0f, new Color(0.50f, 0.57f, 1f)),
                new SongDefinition("lemonade", "LEMONADE", "aespa · 에스파", "lemonade", 128f, 0.1695f, 69.0f, 89.0f, new Color(1f, 0.72f, 0.20f)),
                new SongDefinition("rude", "RUDE!", "Hearts2Hearts · 하츠투하츠", "rude", 128f, 0.3805f, 170.0f, 198.0f, new Color(0.78f, 0.37f, 0.96f))
            };
        }

        private void BuildInterface()
        {
            GameObject canvasObject = new GameObject("Couple Rhythm Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            BuildBackground(canvas.transform);
            BuildPaymentScreen(canvas.transform);
            BuildSongSelect(canvas.transform);
            BuildGameScreen(canvas.transform);
            BuildResultScreen(canvas.transform);
            BuildAdminPanel(canvas.transform);
        }

        private void BuildBackground(Transform parent)
        {
            RectTransform backgroundRect = RuntimeUI.Rect("Background", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            RawImage background = backgroundRect.gameObject.AddComponent<RawImage>();
            background.color = Color.white;
            background.texture = Resources.Load<Texture2D>("CoupleRhythm/Art/couple_rhythm_bg");
            background.raycastTarget = false;

            if (background.texture == null)
            {
                background.color = new Color(1f, 0.65f, 0.78f, 1f);
            }

            AspectRatioFitter fitter = backgroundRect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 16f / 9f;

            Image veil = RuntimeUI.Image("Pink Veil", parent, new Color(0.67f, 0.15f, 0.40f, 0.08f));
            veil.raycastTarget = false;
        }

        private void BuildPaymentScreen(Transform parent)
        {
            paymentRoot = RuntimeUI.Rect("Payment", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            Text eyebrow = RuntimeUI.Text("Eyebrow", paymentRoot, "♥  COUPLE RHYTHM GAME  ♥", 24, new Color(0.76f, 0.20f, 0.46f));
            SetFixed(eyebrow.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -48), new Vector2(900, 42));
            eyebrow.fontStyle = FontStyle.Bold;

            Text title = RuntimeUI.Text("Title", paymentRoot, "먼저 입금을 완료해 주세요", 47, Color.white);
            SetFixed(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -101), new Vector2(1100, 66));
            title.fontStyle = FontStyle.Bold;
            RuntimeUI.AddOutline(title, new Color(0.63f, 0.18f, 0.41f, 0.72f), new Vector2(4, -4));

            RectTransform qrPanel = RuntimeUI.FixedRect("QR Panel", paymentRoot, new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(620, 684));
            Image qrPanelBg = qrPanel.gameObject.AddComponent<Image>();
            qrPanelBg.sprite = RuntimeUI.RoundedSprite;
            qrPanelBg.type = Image.Type.Sliced;
            qrPanelBg.color = new Color(1f, 1f, 1f, 0.96f);
            RuntimeUI.AddShadow(qrPanelBg, new Color(0.48f, 0.10f, 0.32f, 0.25f), new Vector2(0, -12));

            RectTransform qrRect = RuntimeUI.FixedRect("KakaoPay QR", qrPanel, new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(520, 584));
            RawImage qrImage = qrRect.gameObject.AddComponent<RawImage>();
            qrImage.color = Color.white;
            qrImage.raycastTarget = false;
            qrImage.texture = Resources.Load<Texture2D>("CoupleRhythm/Art/KakaoTalk_20260913_214436346");

            if (qrImage.texture == null)
            {
                Text missing = RuntimeUI.Text("Missing QR", qrPanel, "QR 이미지를 불러오지 못했습니다.\nResources/CoupleRhythm/Art 경로를 확인해 주세요.", 23, new Color(0.78f, 0.20f, 0.49f));
                SetFixed(missing.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 140));
                missing.fontStyle = FontStyle.Bold;
            }

            Button confirm = RuntimeUI.Button("Confirm Payment", paymentRoot, "입금 완료  ·  노래 고르기  ♥", new Color(0.93f, 0.28f, 0.58f), Color.white, 28, ConfirmPayment);
            SetFixed(confirm.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0, 61), new Vector2(520, 78));
            RuntimeUI.AddShadow(confirm.targetGraphic, new Color(0.48f, 0.10f, 0.32f, 0.25f), new Vector2(0, -8));
        }

        private void BuildSongSelect(Transform parent)
        {
            selectionRoot = RuntimeUI.Rect("Song Select", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            Text eyebrow = RuntimeUI.Text("Eyebrow", selectionRoot, "♥  COUPLE RHYTHM GAME  ♥", 25, new Color(0.76f, 0.20f, 0.46f));
            SetFixed(eyebrow.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(900, 48));
            eyebrow.fontStyle = FontStyle.Bold;

            Text title = RuntimeUI.Text("Title", selectionRoot, "TWO HEARTS, ONE BEAT", 70, Color.white);
            SetFixed(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -122), new Vector2(1400, 90));
            title.fontStyle = FontStyle.Bold;
            RuntimeUI.AddOutline(title, new Color(0.63f, 0.18f, 0.41f, 0.75f), new Vector2(4, -4));

            Text subtitle = RuntimeUI.Text("Subtitle", selectionRoot, "우리의 노래를 골라볼까요?", 27, ink);
            SetFixed(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -193), new Vector2(700, 46));

            Vector2[] positions =
            {
                new Vector2(-370, 170), new Vector2(370, 170),
                new Vector2(-370, -150), new Vector2(370, -150)
            };
            for (int i = 0; i < songs.Length; i++)
                CreateSongCard(selectionRoot, i, positions[i]);

            RectTransform helpPanel = RuntimeUI.FixedRect("Controls", selectionRoot, new Vector2(0.5f, 0f), new Vector2(0, 57), new Vector2(1110, 96));
            Image helpBg = helpPanel.gameObject.AddComponent<Image>();
            helpBg.sprite = RuntimeUI.RoundedSprite;
            helpBg.type = Image.Type.Sliced;
            helpBg.color = new Color(1f, 1f, 1f, 0.78f);
            Text help = RuntimeUI.Text("Help", helpPanel, "1P  파란 버튼     ·     Together!  두 버튼 동시에     ·     2P  빨간 버튼\n긴 하트는 끝 박자까지 꾹 눌러주세요!", 23, ink);
            help.fontStyle = FontStyle.Bold;

            Button admin = RuntimeUI.Button("Admin", selectionRoot, "⚙  관리자 설정", new Color(0.50f, 0.19f, 0.48f, 0.93f), Color.white, 22, OpenAdmin);
            SetFixed(admin.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-142, 54), new Vector2(230, 64));
        }

        private void CreateSongCard(Transform parent, int index, Vector2 position)
        {
            SongDefinition song = songs[index];
            Button card = RuntimeUI.Button("Song " + song.id, parent, string.Empty, new Color(1f, 1f, 1f, 0.88f), ink, 20, () => SelectSong(index));
            RectTransform rect = card.GetComponent<RectTransform>();
            SetFixed(rect, new Vector2(0.5f, 0.5f), position, new Vector2(680, 272));
            RuntimeUI.AddShadow(card.targetGraphic, new Color(0.48f, 0.10f, 0.32f, 0.22f), new Vector2(0, -10));

            Image stripe = RuntimeUI.Image("Accent", rect, song.accent, RuntimeUI.RoundedSprite);
            SetFixed(stripe.rectTransform, new Vector2(0f, 0.5f), new Vector2(10, 0), new Vector2(20, 224));
            stripe.raycastTarget = false;

            Image heart = RuntimeUI.Image("Heart", rect, song.accent, RuntimeUI.HeartSprite);
            SetFixed(heart.rectTransform, new Vector2(0f, 0.5f), new Vector2(107, 5), new Vector2(120, 106));
            heart.raycastTarget = false;
            RuntimeUI.AddShadow(heart, new Color(song.accent.r, song.accent.g, song.accent.b, 0.3f), new Vector2(0, -7));

            Text number = RuntimeUI.Text("Number", rect, "0" + (index + 1), 22, new Color(song.accent.r, song.accent.g, song.accent.b, 0.8f), TextAnchor.MiddleLeft);
            SetFixed(number.rectTransform, new Vector2(0f, 1f), new Vector2(195, -42), new Vector2(100, 34));
            number.fontStyle = FontStyle.Bold;

            Text title = RuntimeUI.Text("Song Title", rect, song.title, 39, ink, TextAnchor.MiddleLeft);
            SetFixed(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(386, 20), new Vector2(430, 62));
            title.fontStyle = FontStyle.Bold;

            Text artist = RuntimeUI.Text("Artist", rect, song.artist, 22, new Color(0.43f, 0.29f, 0.44f), TextAnchor.MiddleLeft);
            SetFixed(artist.rectTransform, new Vector2(0f, 0.5f), new Vector2(386, -35), new Vector2(430, 46));

            bool hasAudio = Resources.Load<AudioClip>("CoupleRhythm/Audio/" + song.resourceName) != null;
            RectTransform statusPill = RuntimeUI.FixedRect("Audio Status", rect, new Vector2(1f, 0f), new Vector2(-130, 31), new Vector2(230, 33));
            Image statusBg = statusPill.gameObject.AddComponent<Image>();
            statusBg.sprite = RuntimeUI.RoundedSprite;
            statusBg.type = Image.Type.Sliced;
            statusBg.color = new Color(song.accent.r, song.accent.g, song.accent.b, 0.78f);
            string statusLabel = hasAudio
                ? "HIGHLIGHT  " + FormatTime(Mathf.Max(0f, song.highlightStartSeconds - 10f)) + " – " + FormatTime(song.highlightEndSeconds)
                : "DEMO BEAT";
            Text status = RuntimeUI.Text("Label", statusPill, statusLabel, hasAudio ? 13 : 15, Color.white);
            status.fontStyle = FontStyle.Bold;
            status.raycastTarget = false;
        }

        private void BuildGameScreen(Transform parent)
        {
            gameRoot = RuntimeUI.Rect("Gameplay", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image shade = RuntimeUI.Image("Gameplay Shade", gameRoot, new Color(0.16f, 0.03f, 0.16f, 0.34f));
            shade.raycastTarget = false;

            RectTransform hud = RuntimeUI.FixedRect("HUD", gameRoot, new Vector2(0.5f, 1f), new Vector2(0, -64), new Vector2(1720, 104));
            Image hudBg = hud.gameObject.AddComponent<Image>();
            hudBg.sprite = RuntimeUI.RoundedSprite;
            hudBg.type = Image.Type.Sliced;
            hudBg.color = new Color(0.22f, 0.07f, 0.24f, 0.86f);

            songHudText = RuntimeUI.Text("Song", hud, string.Empty, 27, Color.white, TextAnchor.MiddleLeft);
            songHudText.rectTransform.offsetMin = new Vector2(36, 16);
            songHudText.rectTransform.offsetMax = new Vector2(-1060, -16);
            songHudText.fontStyle = FontStyle.Bold;

            accuracyText = RuntimeUI.Text("Accuracy", hud, "정확도  --.-%", 27, Color.white, TextAnchor.MiddleRight);
            accuracyText.rectTransform.offsetMin = new Vector2(1080, 16);
            accuracyText.rectTransform.offsetMax = new Vector2(-250, -16);
            accuracyText.fontStyle = FontStyle.Bold;

            comboText = RuntimeUI.Text("Combo", hud, "", 23, new Color(1f, 0.78f, 0.91f), TextAnchor.MiddleRight);
            comboText.rectTransform.offsetMin = new Vector2(1390, 16);
            comboText.rectTransform.offsetMax = new Vector2(-30, -16);

            RectTransform progress = RuntimeUI.FixedRect("Progress", gameRoot, new Vector2(0.5f, 1f), new Vector2(0, -132), new Vector2(1590, 12));
            Image progressBg = progress.gameObject.AddComponent<Image>();
            progressBg.sprite = RuntimeUI.RoundedSprite;
            progressBg.type = Image.Type.Sliced;
            progressBg.color = new Color(1f, 1f, 1f, 0.28f);
            progressFill = RuntimeUI.Image("Fill", progress, new Color(1f, 0.37f, 0.66f, 1f), RuntimeUI.RoundedSprite);
            progressFill.rectTransform.anchorMin = Vector2.zero;
            progressFill.rectTransform.anchorMax = new Vector2(0f, 1f);
            progressFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            progressFill.rectTransform.offsetMin = Vector2.zero;
            progressFill.rectTransform.offsetMax = Vector2.zero;

            string[] laneTitles = { "1P\nBLUE BUTTON", "Together!\nBLUE + RED", "2P\nRED BUTTON" };
            Color[] laneColors = { blue, purple, red };
            for (int i = 0; i < 3; i++)
            {
                RectTransform lane = RuntimeUI.FixedRect("Lane " + i, gameRoot, new Vector2(0.5f, 0.5f), new Vector2(LaneX[i], -35), new Vector2(278, 765));
                Image laneBg = lane.gameObject.AddComponent<Image>();
                laneBg.sprite = RuntimeUI.RoundedSprite;
                laneBg.type = Image.Type.Sliced;
                laneBg.color = new Color(laneColors[i].r, laneColors[i].g, laneColors[i].b, 0.16f);

                Text label = RuntimeUI.Text("Lane Label", lane, laneTitles[i], 21, Color.white);
                SetFixed(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -59), new Vector2(240, 80));
                label.fontStyle = FontStyle.Bold;

                Image line = RuntimeUI.Image("Guide", lane, new Color(1f, 1f, 1f, 0.18f));
                SetFixed(line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -88), new Vector2(3, 510));
                line.raycastTarget = false;

                targets[i] = RuntimeUI.Image("Target " + i, gameRoot, new Color(laneColors[i].r, laneColors[i].g, laneColors[i].b, 0.43f), RuntimeUI.HeartSprite);
                SetFixed(targets[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(LaneX[i], TargetY), new Vector2(132, 120));
                RuntimeUI.AddOutline(targets[i], Color.white, new Vector2(3f, -3f));
                targets[i].raycastTarget = false;
            }

            noteRoot = RuntimeUI.Rect("Notes", gameRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            noteRoot.SetAsLastSibling();

            judgementText = RuntimeUI.Text("Judgement", gameRoot, string.Empty, 52, Color.white);
            SetFixed(judgementText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -187), new Vector2(700, 90));
            judgementText.fontStyle = FontStyle.Bold;
            RuntimeUI.AddOutline(judgementText, new Color(0.32f, 0.06f, 0.31f, 0.8f), new Vector2(4, -4));
            judgementText.transform.SetAsLastSibling();

            Button select = RuntimeUI.Button("Song Select", gameRoot, "← 곡 선택", new Color(0.25f, 0.09f, 0.28f, 0.9f), Color.white, 20, ShowSongSelect);
            SetFixed(select.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(125, 48), new Vector2(190, 58));

            Button admin = RuntimeUI.Button("Admin", gameRoot, "⚙ 설정", new Color(0.25f, 0.09f, 0.28f, 0.9f), Color.white, 20, OpenAdmin);
            SetFixed(admin.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-110, 48), new Vector2(160, 58));

            countdownRoot = RuntimeUI.Rect("Countdown", gameRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image countdownShade = RuntimeUI.Image("Shade", countdownRoot, new Color(0.17f, 0.02f, 0.18f, 0.44f));
            countdownShade.raycastTarget = false;
            countdownText = RuntimeUI.Text("Number", countdownRoot, "3", 230, Color.white);
            countdownText.fontStyle = FontStyle.Bold;
            RuntimeUI.AddOutline(countdownText, new Color(0.91f, 0.20f, 0.58f, 0.75f), new Vector2(7, -7));
        }

        private void BuildResultScreen(Transform parent)
        {
            resultRoot = RuntimeUI.Rect("Results", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image shade = RuntimeUI.Image("Shade", resultRoot, new Color(0.29f, 0.05f, 0.28f, 0.68f));
            shade.raycastTarget = false;

            fireworksRoot = RuntimeUI.Rect("Fireworks", resultRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            fireworksRoot.gameObject.SetActive(false);

            RectTransform panel = RuntimeUI.FixedRect("Panel", resultRoot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 710));
            Image panelBg = panel.gameObject.AddComponent<Image>();
            panelBg.sprite = RuntimeUI.RoundedSprite;
            panelBg.type = Image.Type.Sliced;
            panelBg.color = new Color(1f, 0.96f, 0.98f, 0.96f);
            RuntimeUI.AddShadow(panelBg, new Color(0.22f, 0.02f, 0.21f, 0.35f), new Vector2(0, -14));

            Text completed = RuntimeUI.Text("Completed", panel, "♥  SONG COMPLETE  ♥", 25, new Color(0.85f, 0.23f, 0.53f));
            SetFixed(completed.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -64), new Vector2(650, 48));
            completed.fontStyle = FontStyle.Bold;

            Text title = RuntimeUI.Text("Title", panel, "우리의 정확도!", 52, ink);
            SetFixed(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -126), new Vector2(650, 70));
            title.fontStyle = FontStyle.Bold;

            resultAccuracyText = RuntimeUI.Text("Final Accuracy", panel, "0.0%", 83, new Color(0.79f, 0.21f, 0.51f));
            SetFixed(resultAccuracyText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -235), new Vector2(650, 105));
            resultAccuracyText.fontStyle = FontStyle.Bold;

            resultStatsText = RuntimeUI.Text("Stats", panel, string.Empty, 28, ink);
            SetFixed(resultStatsText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -25), new Vector2(650, 205));

            resultRewardText = RuntimeUI.Text("Reward", panel, string.Empty, 24, new Color(0.79f, 0.21f, 0.51f));
            SetFixed(resultRewardText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -187), new Vector2(670, 90));
            resultRewardText.fontStyle = FontStyle.Bold;

            Button nextGame = RuntimeUI.Button("Next Game", panel, "다음 게임하기  ♥", new Color(0.93f, 0.28f, 0.58f), Color.white, 26, ShowPayment);
            SetFixed(nextGame.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0, 72), new Vector2(420, 76));

            celebrationText = RuntimeUI.Text("Celebration", resultRoot, "축하합니다!", 62, new Color(1f, 0.86f, 0.28f));
            SetFixed(celebrationText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -76), new Vector2(920, 100));
            celebrationText.fontStyle = FontStyle.Bold;
            celebrationText.raycastTarget = false;
            RuntimeUI.AddOutline(celebrationText, new Color(0.67f, 0.08f, 0.38f, 0.95f), new Vector2(5f, -5f));
            celebrationText.gameObject.SetActive(false);
        }

        private void BuildAdminPanel(Transform parent)
        {
            adminRoot = RuntimeUI.Rect("Admin Settings", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image shade = RuntimeUI.Image("Modal Shade", adminRoot, new Color(0.10f, 0.01f, 0.10f, 0.78f));
            Button dismiss = shade.gameObject.AddComponent<Button>();
            dismiss.onClick.AddListener(CloseAdmin);

            RectTransform panel = RuntimeUI.FixedRect("Panel", adminRoot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1280, 870));
            Image panelBg = panel.gameObject.AddComponent<Image>();
            panelBg.sprite = RuntimeUI.RoundedSprite;
            panelBg.type = Image.Type.Sliced;
            panelBg.color = new Color(1f, 0.96f, 0.98f, 0.985f);
            RuntimeUI.AddShadow(panelBg, new Color(0.07f, 0f, 0.08f, 0.5f), new Vector2(0, -16));

            Text title = RuntimeUI.Text("Title", panel, "관리자 설정", 45, ink, TextAnchor.MiddleLeft);
            SetFixed(title.rectTransform, new Vector2(0f, 1f), new Vector2(195, -73), new Vector2(310, 68));
            title.fontStyle = FontStyle.Bold;

            Button close = RuntimeUI.Button("Close", panel, "×", new Color(0.84f, 0.72f, 0.81f), ink, 36, CloseAdmin);
            SetFixed(close.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-58, -58), new Vector2(56, 56));

            Text judgementHeader = RuntimeUI.Text("Judgement Header", panel, "판정 · 노트 설정", 27, new Color(0.78f, 0.20f, 0.49f), TextAnchor.MiddleLeft);
            SetFixed(judgementHeader.rectTransform, new Vector2(0f, 1f), new Vector2(332, -155), new Vector2(500, 45));
            judgementHeader.fontStyle = FontStyle.Bold;

            CreateSliderRow(panel, "PERFECT 판정 폭", "ms", 35, 250, settings.PerfectWindowMs, new Vector2(-315, 225), value =>
            {
                settings.PerfectWindowMs = value;
                if (settings.GoodWindowMs < value + 20f)
                    settings.GoodWindowMs = value + 20f;
            }, true);
            CreateSliderRow(panel, "GOOD 판정 폭", "ms", 80, 400, settings.GoodWindowMs, new Vector2(-315, 80), value => settings.GoodWindowMs = Mathf.Max(value, settings.PerfectWindowMs + 20f), true);
            CreateSliderRow(panel, "보라 하트 동시입력", "ms", 35, 300, settings.DuetSyncWindowMs, new Vector2(-315, -65), value => settings.DuetSyncWindowMs = value, true);
            CreateSliderRow(panel, "음원 판정 오프셋", "ms", -300, 300, settings.AudioOffsetMs, new Vector2(-315, -210), value => settings.AudioOffsetMs = value, true);
            CreateSliderRow(panel, "노트 밀도", "단계", 1, 4, settings.HeartsPerBeat, new Vector2(-315, -355), value => settings.HeartsPerBeat = Mathf.RoundToInt(value), true);

            Image divider = RuntimeUI.Image("Divider", panel, new Color(0.65f, 0.39f, 0.60f, 0.25f));
            SetFixed(divider.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -24), new Vector2(2, 650));

            Text bpmHeader = RuntimeUI.Text("BPM Header", panel, "곡별 BPM", 27, new Color(0.52f, 0.28f, 0.67f), TextAnchor.MiddleLeft);
            SetFixed(bpmHeader.rectTransform, new Vector2(0.5f, 1f), new Vector2(335, -155), new Vector2(500, 45));
            bpmHeader.fontStyle = FontStyle.Bold;

            for (int i = 0; i < songs.Length; i++)
            {
                int captured = i;
                CreateSliderRow(panel, songs[i].title + "  BPM", string.Empty, 60, 220, settings.SongBpms[i], new Vector2(325, 190 - i * 142), value => settings.SongBpms[captured] = value, true);
            }

            adminHelpText = RuntimeUI.Text("Admin Help", panel, "닫으면 자동 저장됩니다", 19, new Color(0.47f, 0.32f, 0.47f));
            SetFixed(adminHelpText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 32), new Vector2(890, 40));

            Button reset = RuntimeUI.Button("Reset", panel, "기본값", new Color(0.69f, 0.55f, 0.66f), Color.white, 20, ResetAdminSettings);
            SetFixed(reset.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(105, 58), new Vector2(145, 58));
            Button friendly = RuntimeUI.Button("Friendly", panel, "친절 모드 ♥", new Color(0.93f, 0.34f, 0.62f), Color.white, 20, ApplyFriendlyMode);
            SetFixed(friendly.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(-126, 58), new Vector2(188, 58));
        }

        private void CreateSliderRow(Transform parent, string label, string unit, float min, float max, float initial, Vector2 position, UnityEngine.Events.UnityAction<float> onChanged, bool wholeNumbers)
        {
            RectTransform row = RuntimeUI.FixedRect(label, parent, new Vector2(0.5f, 0.5f), position, new Vector2(520, 126));
            Text labelText = RuntimeUI.Text("Label", row, label, 21, ink, TextAnchor.MiddleLeft);
            labelText.rectTransform.offsetMin = new Vector2(8, 67);
            labelText.rectTransform.offsetMax = new Vector2(-140, -8);
            labelText.fontStyle = FontStyle.Bold;

            Text valueText = RuntimeUI.Text("Value", row, string.Empty, 21, new Color(0.80f, 0.22f, 0.51f), TextAnchor.MiddleRight);
            valueText.rectTransform.offsetMin = new Vector2(365, 67);
            valueText.rectTransform.offsetMax = new Vector2(-8, -8);
            valueText.fontStyle = FontStyle.Bold;

            RectTransform sliderRect = RuntimeUI.Rect("Slider", row, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(8, 23), new Vector2(-8, 55));
            Slider slider = sliderRect.gameObject.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;

            Image track = RuntimeUI.Image("Track", sliderRect, new Color(0.55f, 0.34f, 0.52f, 0.22f), RuntimeUI.RoundedSprite);
            track.rectTransform.offsetMin = new Vector2(0, 10);
            track.rectTransform.offsetMax = new Vector2(0, -10);
            Image fill = RuntimeUI.Image("Fill", sliderRect, new Color(0.92f, 0.31f, 0.61f), RuntimeUI.RoundedSprite);
            fill.rectTransform.offsetMin = new Vector2(0, 10);
            fill.rectTransform.offsetMax = new Vector2(0, -10);
            Image handle = RuntimeUI.Image("Handle", sliderRect, Color.white, RuntimeUI.CircleSprite);
            SetFixed(handle.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(31, 31));
            RuntimeUI.AddShadow(handle, new Color(0.45f, 0.14f, 0.38f, 0.25f), new Vector2(0, -3));

            slider.targetGraphic = handle;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.direction = Slider.Direction.LeftToRight;
            slider.value = initial;

            void Refresh(float value)
            {
                float displayed = wholeNumbers ? Mathf.Round(value) : value;
                valueText.text = displayed.ToString("0") + (string.IsNullOrEmpty(unit) ? string.Empty : " " + unit);
                onChanged?.Invoke(displayed);
            }
            slider.onValueChanged.AddListener(Refresh);
            Refresh(initial);
            adminSliders.Add(slider);
        }

        private void SelectSong(int index)
        {
            if (adminOpen)
                CloseAdmin();
            StopAllCoroutines();
            musicSource.Stop();
            selectedSongIndex = Mathf.Clamp(index, 0, songs.Length - 1);
            currentSongBpm = settings.SongBpms[selectedSongIndex];
            StartCoroutine(StartSongRoutine());
        }

        private IEnumerator StartSongRoutine()
        {
            ClearNotes();
            ResetRoundStats();
            state = GameState.Countdown;
            paymentRoot.gameObject.SetActive(false);
            selectionRoot.gameObject.SetActive(false);
            resultRoot.gameObject.SetActive(false);
            gameRoot.gameObject.SetActive(true);
            countdownRoot.gameObject.SetActive(true);

            SongDefinition song = songs[selectedSongIndex];
            songHudText.text = song.title + "  ·  " + song.artist;
            AudioClip clip = Resources.Load<AudioClip>("CoupleRhythm/Audio/" + song.resourceName);
            usingDemoClip = clip == null;
            if (usingDemoClip)
                clip = GetOrCreateDemoClip(selectedSongIndex, currentSongBpm);
            musicSource.clip = clip;
            if (usingDemoClip)
            {
                clipPlaybackStart = 0f;
                clipPlaybackEnd = clip.length;
            }
            else
            {
                clipPlaybackStart = Mathf.Clamp(song.highlightStartSeconds - 10f, 0f, Mathf.Max(0f, clip.length - 1f));
                clipPlaybackEnd = Mathf.Clamp(song.highlightEndSeconds, clipPlaybackStart + 1f, clip.length);
            }
            songDuration = clipPlaybackEnd - clipPlaybackStart;

            for (int number = 3; number >= 1; number--)
            {
                countdownText.text = number.ToString();
                countdownText.color = Color.white;
                countdownText.rectTransform.localScale = Vector3.one * 0.52f;

                if (number == 1)
                    BeginPlayback();

                const float duration = 0.82f;
                for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
                {
                    float t = Mathf.Clamp01(elapsed / duration);
                    float eased = 1f - (1f - t) * (1f - t);
                    countdownText.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.52f, 2.15f, eased);
                    countdownText.color = new Color(1f, 1f, 1f, 1f - eased);
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(0.12f);
            }

            countdownRoot.gameObject.SetActive(false);
        }

        private void BeginPlayback()
        {
            float secondsPerBeat = 60f / Mathf.Max(1f, currentSongBpm);
            SongDefinition song = songs[selectedSongIndex];
            float beatOffset = usingDemoClip ? 0f : song.beatOffsetSeconds;
            float firstAbsoluteBeat = beatOffset + Mathf.Ceil((clipPlaybackStart + TravelTime - beatOffset) / secondsPerBeat) * secondsPerBeat;
            BuildRhythmChart(firstAbsoluteBeat - clipPlaybackStart);
            nextNoteIndex = 0;
            musicSource.volume = 0f;
            musicSource.time = clipPlaybackStart;
            musicSource.Play();
            playbackStartRealtime = Time.unscaledTime;
            state = GameState.Playing;
        }

        private void SpawnUpcomingNotes(float songTime)
        {
            while (nextNoteIndex < chartNotes.Count && chartNotes[nextNoteIndex].TargetTime <= songTime + TravelTime)
            {
                ChartNote chartNote = chartNotes[nextNoteIndex];
                HeartKind kind = chartNote.Kind;
                int lane = kind == HeartKind.PlayerOne ? 0 : kind == HeartKind.PlayerTwo ? 2 : 1;
                Color color = kind == HeartKind.PlayerOne ? blue : kind == HeartKind.PlayerTwo ? red : purple;
                HeartNoteView note = HeartNoteView.Create(noteRoot, kind, chartNote.TargetTime, chartNote.HoldDuration, LaneX[lane], TargetY, SpawnY, TravelTime, color);
                notes.Add(note);
                nextNoteIndex++;
            }
        }

        private void BuildRhythmChart(float firstTargetTime)
        {
            chartNotes.Clear();
            maximumAccuracyWeight = 0f;
            float secondsPerBeat = 60f / Mathf.Max(1f, currentSongBpm);
            float lastTargetTime = songDuration - 0.35f;
            int density = Mathf.Clamp(settings.HeartsPerBeat, 1, 4);
            int seed = unchecked(System.Environment.TickCount ^ selectedSongIndex * 486187739 ^ ++chartGeneration * 16777619);
            System.Random random = new System.Random(seed);

            float playerOneBlockedUntilBeat = -1f;
            float playerTwoBlockedUntilBeat = -1f;
            int previousTemplate = -1;
            int measuresSinceBurst = 1;
            int measuresSinceHold = 0;
            int measuresSinceDuet = 1;
            int lastSoloPlayer = 0;
            int soloRunLength = 0;

            for (int measure = 0; ; measure++)
            {
                float measureStartBeat = measure * 4f;
                if (firstTargetTime + measureStartBeat * secondsPerBeat >= lastTargetTime)
                    break;

                bool forceBurst = measuresSinceBurst >= 2;
                int templateIndex = ChooseRhythmTemplate(random, previousTemplate, density, forceBurst);
                RhythmTemplate template = RhythmTemplates[templateIndex];
                previousTemplate = templateIndex;
                measuresSinceBurst = template.BurstLength >= 3 ? 0 : measuresSinceBurst + 1;

                double singleLaneBurstChance = density >= 4 ? 0.34 : density == 3 ? 0.46 : 0.58;
                bool useSingleLaneBurst = random.NextDouble() < singleLaneBurstChance;
                int burstFirstPlayer = random.Next(0, 2) == 0 ? 1 : 2;
                bool holdCreatedThisMeasure = false;
                bool duetCreatedThisMeasure = false;

                for (int noteIndex = 0; noteIndex < template.BeatOffsets.Length; noteIndex++)
                {
                    float absoluteBeat = measureStartBeat + template.BeatOffsets[noteIndex];
                    float targetTime = firstTargetTime + absoluteBeat * secondsPerBeat;
                    if (targetTime >= lastTargetTime)
                        break;

                    bool playerOneFree = absoluteBeat + 0.001f >= playerOneBlockedUntilBeat;
                    bool playerTwoFree = absoluteBeat + 0.001f >= playerTwoBlockedUntilBeat;
                    if (!playerOneFree && !playerTwoFree)
                        continue;

                    bool isBurstNote = template.IsBurstNote(noteIndex);
                    HeartKind kind;
                    if (isBurstNote)
                    {
                        int burstOffset = noteIndex - template.BurstStart;
                        int desiredPlayer = useSingleLaneBurst
                            ? burstFirstPlayer
                            : (burstOffset % 2 == 0 ? burstFirstPlayer : 3 - burstFirstPlayer);
                        if (desiredPlayer == 1 && !playerOneFree)
                            desiredPlayer = 2;
                        else if (desiredPlayer == 2 && !playerTwoFree)
                            desiredPlayer = 1;
                        kind = desiredPlayer == 1 ? HeartKind.PlayerOne : HeartKind.PlayerTwo;
                    }
                    else if (playerOneFree && playerTwoFree &&
                        ((measuresSinceDuet >= 2 && !duetCreatedThisMeasure) || random.NextDouble() < 0.18 + density * 0.025))
                    {
                        kind = HeartKind.Duet;
                        duetCreatedThisMeasure = true;
                    }
                    else
                    {
                        int player;
                        if (!playerOneFree)
                            player = 2;
                        else if (!playerTwoFree)
                            player = 1;
                        else if (soloRunLength >= 2)
                            player = 3 - lastSoloPlayer;
                        else
                            player = random.Next(0, 2) == 0 ? 1 : 2;
                        kind = player == 1 ? HeartKind.PlayerOne : HeartKind.PlayerTwo;
                    }

                    if (kind == HeartKind.Duet)
                    {
                        lastSoloPlayer = 0;
                        soloRunLength = 0;
                    }
                    else
                    {
                        int player = kind == HeartKind.PlayerOne ? 1 : 2;
                        soloRunLength = player == lastSoloPlayer ? soloRunLength + 1 : 1;
                        lastSoloPlayer = player;
                    }

                    float holdDuration = 0f;
                    bool forceHold = measuresSinceHold >= 3;
                    bool canBecomeHold = !isBurstNote && kind != HeartKind.Duet && !holdCreatedThisMeasure;
                    if (canBecomeHold && (forceHold || random.NextDouble() < 0.19))
                    {
                        float durationBeats;
                        int durationChoice = random.Next(0, 3);
                        if (durationChoice == 0)
                            durationBeats = 1f;
                        else if (durationChoice == 1)
                            durationBeats = 1.5f;
                        else
                            durationBeats = 2f;

                        if (targetTime + durationBeats * secondsPerBeat < lastTargetTime)
                        {
                            holdDuration = durationBeats * secondsPerBeat;
                            float blockedUntilBeat = absoluteBeat + durationBeats + 0.5f;
                            if (kind == HeartKind.PlayerOne)
                                playerOneBlockedUntilBeat = blockedUntilBeat;
                            else
                                playerTwoBlockedUntilBeat = blockedUntilBeat;
                            holdCreatedThisMeasure = true;
                        }
                    }

                    chartNotes.Add(new ChartNote(kind, targetTime, holdDuration));
                    maximumAccuracyWeight += GetAccuracyWeight(kind, holdDuration > 0f);
                }

                measuresSinceHold = holdCreatedThisMeasure ? 0 : measuresSinceHold + 1;
                measuresSinceDuet = duetCreatedThisMeasure ? 0 : measuresSinceDuet + 1;
            }
        }

        private static int ChooseRhythmTemplate(System.Random random, int previousTemplate, int density, bool forceBurst)
        {
            int minimumTemplateDensity = density >= 4 ? 3 : density == 3 ? 2 : 1;
            for (int attempt = 0; attempt < 32; attempt++)
            {
                int index = random.Next(0, RhythmTemplates.Length);
                RhythmTemplate candidate = RhythmTemplates[index];
                if (index == previousTemplate || candidate.MinimumDensity > density || candidate.MinimumDensity < minimumTemplateDensity)
                    continue;
                if (forceBurst && candidate.BurstLength < 3)
                    continue;
                return index;
            }

            for (int index = 0; index < RhythmTemplates.Length; index++)
            {
                RhythmTemplate candidate = RhythmTemplates[index];
                if (index != previousTemplate && candidate.MinimumDensity >= minimumTemplateDensity && candidate.MinimumDensity <= density && (!forceBurst || candidate.BurstLength >= 3))
                    return index;
            }
            return 2;
        }

        private void TryHit(int player)
        {
            float songTime = CurrentSongTime;
            HeartNoteView best = null;
            float bestError = float.MaxValue;
            for (int i = 0; i < notes.Count; i++)
            {
                HeartNoteView note = notes[i];
                if (note == null || !note.CanReceive(player, songTime, settings))
                    continue;
                float error = Mathf.Abs(songTime - note.TargetTime);
                if (error < bestError)
                {
                    bestError = error;
                    best = note;
                }
            }

            PulseTarget(player == 1 ? 0 : 2);
            if (best == null)
            {
                RegisterWrongPress(player, songTime);
                return;
            }

            if (!best.RegisterPress(player, songTime, settings, out HitRating rating))
                return;

            if (rating == HitRating.None)
            {
                if (best.Kind == HeartKind.Duet)
                {
                    PulseTarget(1);
                    ShowJudgement(player == 1 ? "1P READY!" : "2P READY!", player == 1 ? blue : red);
                }
                else if (best.IsHold)
                {
                    ShowJudgement(player == 1 ? "1P HOLD!" : "2P HOLD!", player == 1 ? blue : red);
                }
                return;
            }

            ResolveNote(best, rating);
        }

        private void RegisterWrongPress(int player, float songTime)
        {
            wrongPressCount++;
            ShowJudgement("WRONG!", player == 1 ? blue : red);
            UpdateHud(songTime);
        }

        private void UpdateNotes(float songTime)
        {
            Keyboard keyboard = Keyboard.current;
            bool playerOneHeld = keyboard != null && keyboard.aKey.isPressed;
            bool playerTwoHeld = keyboard != null && keyboard.lKey.isPressed;

            for (int i = notes.Count - 1; i >= 0; i--)
            {
                HeartNoteView note = notes[i];
                if (note == null)
                {
                    notes.RemoveAt(i);
                    continue;
                }
                if (!note.Resolved)
                    note.UpdateVisual(songTime);
                bool correctButtonHeld = note.Kind == HeartKind.PlayerOne ? playerOneHeld : playerTwoHeld;
                if (note.TryCompleteHold(songTime, correctButtonHeld, settings, out HitRating holdRating))
                    ResolveNote(note, holdRating);
                else if (note.TryExpire(songTime, settings))
                    ResolveNote(note, HitRating.Miss);
                if (note.Resolved)
                    notes.RemoveAt(i);
            }
        }

        private void ResolveNote(HeartNoteView note, HitRating rating)
        {
            float accuracyWeight = GetAccuracyWeight(note.Kind, note.IsHold);
            judgedAccuracyWeight += accuracyWeight;

            if (rating == HitRating.Perfect)
            {
                earnedAccuracyWeight += accuracyWeight;
                perfectCount++;
                combo++;
                ShowJudgement(note.IsHold ? "PERFECT HOLD ♥" : note.Kind == HeartKind.Duet ? "PERFECT TOGETHER ♥" : "PERFECT!", new Color(1f, 0.85f, 0.28f));
            }
            else if (rating == HitRating.Good)
            {
                earnedAccuracyWeight += accuracyWeight * GoodAccuracyRatio;
                goodCount++;
                combo++;
                ShowJudgement(note.IsHold ? "GOOD HOLD" : note.Kind == HeartKind.Duet ? "GOOD TOGETHER" : "GOOD", cream);
            }
            else
            {
                missCount++;
                combo = 0;
                ShowJudgement(note.IsHold ? "HOLD MISS" : note.Kind == HeartKind.Duet ? "MISS · 둘이 함께!" : "MISS", new Color(1f, 0.50f, 0.62f));
            }

            maxCombo = Mathf.Max(maxCombo, combo);
            note.ResolveAnimation(rating);
            int lane = note.Kind == HeartKind.PlayerOne ? 0 : note.Kind == HeartKind.PlayerTwo ? 2 : 1;
            PulseTarget(lane);
            UpdateHud(CurrentSongTime);
        }

        private void ShowJudgement(string message, Color color)
        {
            if (judgementRoutine != null)
                StopCoroutine(judgementRoutine);
            judgementRoutine = StartCoroutine(JudgementRoutine(message, color));
        }

        private IEnumerator JudgementRoutine(string message, Color color)
        {
            judgementText.text = message;
            judgementText.color = color;
            judgementText.rectTransform.localScale = Vector3.one * 0.72f;
            for (float elapsed = 0; elapsed < 0.45f; elapsed += Time.unscaledDeltaTime)
            {
                float t = elapsed / 0.45f;
                judgementText.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.72f, 1.06f, 1f - Mathf.Pow(1f - Mathf.Clamp01(t * 2f), 2f));
                judgementText.color = new Color(color.r, color.g, color.b, 1f - Mathf.Clamp01((t - 0.48f) / 0.52f));
                yield return null;
            }
            judgementText.text = string.Empty;
            judgementRoutine = null;
        }

        private void PulseTarget(int lane)
        {
            if (lane < 0 || lane >= targets.Length || targets[lane] == null)
                return;
            StartCoroutine(TargetPulseRoutine(targets[lane].rectTransform));
        }

        private IEnumerator TargetPulseRoutine(RectTransform target)
        {
            Vector3 original = Vector3.one;
            for (float elapsed = 0; elapsed < 0.16f; elapsed += Time.unscaledDeltaTime)
            {
                float t = elapsed / 0.16f;
                float scale = 1f + Mathf.Sin(t * Mathf.PI) * 0.22f;
                target.localScale = original * scale;
                yield return null;
            }
            target.localScale = original;
        }

        private void UpdateHud(float songTime)
        {
            accuracyText.text = judgedAccuracyWeight > 0f || wrongPressCount > 0
                ? "정확도  " + FormatAccuracy(CalculateAccuracy(false))
                : "정확도  --.-%";
            comboText.text = combo > 1 ? combo + " COMBO ♥" : string.Empty;
            float progress = songDuration > 0 ? Mathf.Clamp01(songTime / songDuration) : 0f;
            progressFill.rectTransform.anchorMax = new Vector2(progress, 1f);
        }

        private void UpdateHighlightFade()
        {
            if (!musicSource.isPlaying)
                return;
            float absoluteTime = CurrentAbsoluteAudioTime;
            float fadeIn = Mathf.Clamp01((absoluteTime - clipPlaybackStart) / 0.28f);
            float fadeOut = Mathf.Clamp01((clipPlaybackEnd - absoluteTime) / 0.75f);
            musicSource.volume = 0.85f * Mathf.Min(fadeIn, fadeOut);
        }

        private void FinishSong()
        {
            if (state != GameState.Playing)
                return;
            state = GameState.Results;
            ClearNotes();
            gameRoot.gameObject.SetActive(false);
            resultRoot.gameObject.SetActive(true);
            float finalAccuracy = Mathf.Round(CalculateAccuracy(true) * 10f) / 10f;
            bool premiumPrizeEarned = finalAccuracy >= PremiumPrizeAccuracyThreshold;
            bool prizeEarned = finalAccuracy >= PrizeAccuracyThreshold;
            ResetCelebration();
            resultAccuracyText.text = FormatAccuracy(finalAccuracy);
            resultStatsText.text =
                "PERFECT     " + perfectCount + "\n" +
                "GOOD          " + goodCount + "\n" +
                "MISS           " + missCount + "\n" +
                "WRONG PRESS  " + wrongPressCount + "\n" +
                "MAX COMBO  " + maxCombo;
            if (premiumPrizeEarned)
            {
                resultRewardText.text = "♥ 100% 달성! 아주 좋은 상품을 드립니다 ♥\n관리자에게 이 화면을 보여주세요!";
                resultRewardText.color = new Color(0.93f, 0.45f, 0.08f);
                StartCoroutine(CelebrationRoutine());
            }
            else if (prizeEarned)
            {
                resultRewardText.text = "♥ 상품 획득 성공! 관리자에게 보여주세요 ♥";
                resultRewardText.color = new Color(0.89f, 0.22f, 0.52f);
            }
            else
            {
                resultRewardText.text = "상품 획득 기준은 정확도 " + PrizeAccuracyThreshold.ToString("0") + "% 이상입니다.";
                resultRewardText.color = new Color(0.46f, 0.31f, 0.47f);
            }
        }

        private IEnumerator CelebrationRoutine()
        {
            fireworksRoot.gameObject.SetActive(true);
            celebrationText.gameObject.SetActive(true);
            celebrationText.rectTransform.localScale = Vector3.one * 0.35f;

            for (float elapsed = 0f; elapsed < 0.55f; elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / 0.55f);
                float overshoot = 1f + Mathf.Sin(t * Mathf.PI) * (1f - t) * 0.5f;
                celebrationText.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.35f, overshoot, 1f - Mathf.Pow(1f - t, 3f));
                yield return null;
            }
            celebrationText.rectTransform.localScale = Vector3.one;

            Vector2[] burstPositions =
            {
                new Vector2(-670f, 300f),
                new Vector2(680f, 270f),
                new Vector2(-760f, -80f),
                new Vector2(760f, -110f),
                new Vector2(-510f, 390f),
                new Vector2(520f, 390f)
            };

            for (int i = 0; i < burstPositions.Length; i++)
            {
                StartCoroutine(FireworkBurstRoutine(burstPositions[i], i * 7919 + 104729));
                yield return new WaitForSecondsRealtime(0.2f);
            }
        }

        private IEnumerator FireworkBurstRoutine(Vector2 origin, int seed)
        {
            const int particleCount = 22;
            const float duration = 1.15f;
            System.Random random = new System.Random(seed);
            RectTransform[] particles = new RectTransform[particleCount];
            Image[] particleImages = new Image[particleCount];
            Vector2[] directions = new Vector2[particleCount];
            float[] distances = new float[particleCount];
            float[] rotations = new float[particleCount];
            Color[] palette =
            {
                new Color(1f, 0.82f, 0.15f),
                new Color(1f, 0.30f, 0.58f),
                new Color(0.26f, 0.76f, 1f),
                new Color(0.72f, 0.39f, 1f),
                new Color(1f, 0.49f, 0.18f),
                new Color(0.28f, 0.95f, 0.70f)
            };

            for (int i = 0; i < particleCount; i++)
            {
                float angle = Mathf.PI * 2f * i / particleCount + (float)(random.NextDouble() - 0.5) * 0.18f;
                directions[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                distances[i] = Mathf.Lerp(125f, 235f, (float)random.NextDouble());
                rotations[i] = Mathf.Lerp(-220f, 220f, (float)random.NextDouble());
                Color color = palette[random.Next(palette.Length)];
                Sprite sprite = i % 5 == 0 ? RuntimeUI.HeartSprite : RuntimeUI.CircleSprite;
                Image particle = RuntimeUI.Image("Firework Spark", fireworksRoot, color, sprite);
                float size = sprite == RuntimeUI.HeartSprite
                    ? Mathf.Lerp(24f, 39f, (float)random.NextDouble())
                    : Mathf.Lerp(12f, 24f, (float)random.NextDouble());
                SetFixed(particle.rectTransform, new Vector2(0.5f, 0.5f), origin, Vector2.one * size);
                particle.raycastTarget = false;
                particles[i] = particle.rectTransform;
                particleImages[i] = particle;
            }

            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                float expansion = 1f - Mathf.Pow(1f - t, 3f);
                float alpha = 1f - Mathf.Clamp01((t - 0.48f) / 0.52f);
                float scale = Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(t * 7f)) * Mathf.Lerp(1f, 0.35f, t);

                for (int i = 0; i < particleCount; i++)
                {
                    particles[i].anchoredPosition = origin + directions[i] * distances[i] * expansion + Vector2.down * (115f * t * t);
                    particles[i].localRotation = Quaternion.Euler(0f, 0f, rotations[i] * t);
                    particles[i].localScale = Vector3.one * scale;
                    Color color = particleImages[i].color;
                    particleImages[i].color = new Color(color.r, color.g, color.b, alpha);
                }
                yield return null;
            }

            for (int i = 0; i < particleCount; i++)
                Destroy(particles[i].gameObject);
        }

        private void ResetCelebration()
        {
            if (celebrationText != null)
            {
                celebrationText.gameObject.SetActive(false);
                celebrationText.rectTransform.localScale = Vector3.one;
            }

            if (fireworksRoot == null)
                return;
            fireworksRoot.gameObject.SetActive(false);
            for (int i = fireworksRoot.childCount - 1; i >= 0; i--)
                Destroy(fireworksRoot.GetChild(i).gameObject);
        }

        private void ShowSongSelect()
        {
            StopAllCoroutines();
            ResetCelebration();
            musicSource.Stop();
            ClearNotes();
            state = GameState.SongSelect;
            adminOpen = false;
            paymentRoot.gameObject.SetActive(false);
            selectionRoot.gameObject.SetActive(true);
            gameRoot.gameObject.SetActive(false);
            resultRoot.gameObject.SetActive(false);
            adminRoot.gameObject.SetActive(false);
        }

        private void ShowPayment()
        {
            StopAllCoroutines();
            ResetCelebration();
            musicSource.Stop();
            ClearNotes();
            state = GameState.Payment;
            adminOpen = false;
            paymentRoot.gameObject.SetActive(true);
            selectionRoot.gameObject.SetActive(false);
            gameRoot.gameObject.SetActive(false);
            resultRoot.gameObject.SetActive(false);
            adminRoot.gameObject.SetActive(false);
        }

        private void ConfirmPayment()
        {
            ShowSongSelect();
        }

        private void ResetRoundStats()
        {
            combo = 0;
            maxCombo = 0;
            perfectCount = 0;
            goodCount = 0;
            missCount = 0;
            wrongPressCount = 0;
            earnedAccuracyWeight = 0f;
            judgedAccuracyWeight = 0f;
            maximumAccuracyWeight = 0f;
            accuracyText.text = "정확도  --.-%";
            comboText.text = string.Empty;
            judgementText.text = string.Empty;
            progressFill.rectTransform.anchorMax = new Vector2(0f, 1f);
        }

        private void ClearNotes()
        {
            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i] != null)
                    Destroy(notes[i].gameObject);
            }
            notes.Clear();
            chartNotes.Clear();
            nextNoteIndex = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                    targets[i].rectTransform.localScale = Vector3.one;
            }
        }

        private void OpenAdmin()
        {
            if (adminOpen || state == GameState.Countdown)
                return;
            adminOpen = true;
            RefreshAdminSliders();
            resumeMusicAfterAdmin = state == GameState.Playing && musicSource.isPlaying;
            if (resumeMusicAfterAdmin)
                musicSource.Pause();
            adminRoot.gameObject.SetActive(true);
            adminRoot.SetAsLastSibling();
        }

        private void CloseAdmin()
        {
            if (!adminOpen)
                return;
            settings.Clamp();
            settings.Save();
            adminOpen = false;
            adminRoot.gameObject.SetActive(false);
            if (resumeMusicAfterAdmin && state == GameState.Playing)
                musicSource.UnPause();
            resumeMusicAfterAdmin = false;
        }

        private void ResetAdminSettings()
        {
            settings.ResetToDefaults(songs);
            RefreshAdminSliders();
            adminHelpText.text = "기본값을 저장했습니다.";
        }

        private void ApplyFriendlyMode()
        {
            settings.PerfectWindowMs = 150f;
            settings.GoodWindowMs = 270f;
            settings.DuetSyncWindowMs = 210f;
            settings.HeartsPerBeat = 1;
            settings.Save();
            RefreshAdminSliders();
            adminHelpText.text = "친절 모드 적용 완료 ♥";
        }

        private void RefreshAdminSliders()
        {
            if (adminSliders.Count < 9)
                return;
            float[] values =
            {
                settings.PerfectWindowMs,
                settings.GoodWindowMs,
                settings.DuetSyncWindowMs,
                settings.AudioOffsetMs,
                settings.HeartsPerBeat,
                settings.SongBpms[0],
                settings.SongBpms[1],
                settings.SongBpms[2],
                settings.SongBpms[3]
            };
            for (int i = 0; i < values.Length; i++)
                adminSliders[i].SetValueWithoutNotify(values[i]);
            for (int i = 0; i < values.Length; i++)
                adminSliders[i].onValueChanged.Invoke(values[i]);
        }

        private AudioClip GetOrCreateDemoClip(int songIndex, float bpm)
        {
            int key = songIndex * 1000 + Mathf.RoundToInt(bpm);
            if (demoClips.TryGetValue(key, out AudioClip existing) && existing != null)
                return existing;

            const int sampleRate = 44100;
            const float duration = 45f;
            int length = Mathf.RoundToInt(sampleRate * duration);
            float[] samples = new float[length];
            float beatDuration = 60f / Mathf.Max(1f, bpm);
            int[] scale = { 0, 4, 7, 11, 7, 4, 2, 7 };
            float root = 220f * Mathf.Pow(2f, songIndex / 12f);

            for (int i = 0; i < length; i++)
            {
                float time = i / (float)sampleRate;
                float beatPosition = time / beatDuration;
                float beatPhase = beatPosition - Mathf.Floor(beatPosition);
                int eighth = Mathf.FloorToInt(beatPosition * 2f);
                float eighthPhase = beatPosition * 2f - Mathf.Floor(beatPosition * 2f);

                float kickEnvelope = Mathf.Exp(-beatPhase * 12f);
                float kick = Mathf.Sin(2f * Mathf.PI * (78f - beatPhase * 28f) * time) * kickEnvelope * 0.30f;

                float noteFrequency = root * Mathf.Pow(2f, scale[eighth % scale.Length] / 12f);
                float melodyEnvelope = Mathf.Pow(1f - eighthPhase, 2f);
                float melody = Mathf.Sin(2f * Mathf.PI * noteFrequency * time) * melodyEnvelope * 0.105f;
                float shimmer = Mathf.Sin(2f * Mathf.PI * noteFrequency * 2f * time) * melodyEnvelope * 0.026f;

                float barFade = Mathf.Min(1f, time * 2f) * Mathf.Min(1f, (duration - time) * 1.2f);
                samples[i] = Mathf.Clamp((kick + melody + shimmer) * barFade, -0.78f, 0.78f);
            }

            AudioClip clip = AudioClip.Create("Demo Beat - " + songs[songIndex].title, length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            demoClips[key] = clip;
            return clip;
        }

        private static string FormatTime(float seconds)
        {
            int wholeSeconds = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return (wholeSeconds / 60) + ":" + (wholeSeconds % 60).ToString("D2");
        }

        private float CalculateAccuracy(bool includeUnjudgedNotes)
        {
            float noteWeight = includeUnjudgedNotes ? maximumAccuracyWeight : judgedAccuracyWeight;
            float possibleWeight = noteWeight + wrongPressCount * WrongPressAccuracyWeight;
            if (possibleWeight <= 0.001f)
                return 0f;
            return Mathf.Clamp01(earnedAccuracyWeight / possibleWeight) * 100f;
        }

        private static float GetAccuracyWeight(HeartKind kind, bool isHold)
        {
            float weight = 1f;
            if (kind == HeartKind.Duet)
                weight += 1f;
            if (isHold)
                weight += 1f;
            return weight;
        }

        private static string FormatAccuracy(float value)
        {
            return value.ToString("0.0") + "%";
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            InputSystemUIInputModule inputModule = eventSystem.AddComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
            DontDestroyOnLoad(eventSystem);
        }

        private static void SetFixed(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
