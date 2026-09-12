using UnityEngine;
using UnityEngine.UI;

namespace CoupleRhythm
{
    public sealed class HeartNoteView : MonoBehaviour
    {
        private RectTransform rect;
        private Image heart;
        private Image glow;
        private Image p1Ready;
        private Image p2Ready;
        private Image holdTail;
        private Image holdEnd;
        private float laneX;
        private float targetY;
        private float spawnY;
        private float travelTime;
        private float holdTailLength;
        private HitRating holdStartRating;

        public HeartKind Kind { get; private set; }
        public float TargetTime { get; private set; }
        public float HoldDuration { get; private set; }
        public float HoldEndTime => TargetTime + HoldDuration;
        public bool IsHold => HoldDuration > 0.01f;
        public bool HoldStarted { get; private set; }
        public bool Resolved { get; private set; }
        public bool PlayerOneReady { get; private set; }
        public bool PlayerTwoReady { get; private set; }
        public float PlayerOnePressTime { get; private set; }
        public float PlayerTwoPressTime { get; private set; }

        public static HeartNoteView Create(Transform parent, HeartKind kind, float targetTime, float holdDuration, float laneX, float targetY, float spawnY, float travelTime, Color color)
        {
            RectTransform root = RuntimeUI.FixedRect("Heart Note", parent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(112, 102));
            HeartNoteView note = root.gameObject.AddComponent<HeartNoteView>();
            note.rect = root;
            note.Kind = kind;
            note.TargetTime = targetTime;
            note.HoldDuration = Mathf.Max(0f, holdDuration);
            note.laneX = laneX;
            note.targetY = targetY;
            note.spawnY = spawnY;
            note.travelTime = travelTime;

            if (note.IsHold)
            {
                float travelSpeed = Mathf.Abs(spawnY - targetY) / Mathf.Max(0.01f, travelTime);
                note.holdTailLength = Mathf.Clamp(travelSpeed * note.HoldDuration, 105f, 850f);
                note.holdTail = RuntimeUI.Image("Hold Tail", root, new Color(color.r, color.g, color.b, 0.72f), RuntimeUI.RoundedSprite);
                note.holdTail.type = Image.Type.Sliced;
                RectTransform tailRect = note.holdTail.rectTransform;
                tailRect.anchorMin = new Vector2(0.5f, 0.5f);
                tailRect.anchorMax = new Vector2(0.5f, 0.5f);
                tailRect.pivot = new Vector2(0.5f, 0f);
                tailRect.anchoredPosition = new Vector2(0f, 42f);
                tailRect.sizeDelta = new Vector2(34f, note.holdTailLength);

                note.holdEnd = RuntimeUI.Image("Hold End", root, new Color(color.r, color.g, color.b, 0.95f), RuntimeUI.HeartSprite);
                RectTransform endRect = note.holdEnd.rectTransform;
                endRect.anchorMin = new Vector2(0.5f, 0.5f);
                endRect.anchorMax = new Vector2(0.5f, 0.5f);
                endRect.pivot = new Vector2(0.5f, 0.5f);
                endRect.anchoredPosition = new Vector2(0f, 42f + note.holdTailLength);
                endRect.sizeDelta = new Vector2(58f, 52f);
                RuntimeUI.AddOutline(note.holdEnd, Color.white, new Vector2(2f, -2f));
            }

            note.glow = RuntimeUI.Image("Glow", root, new Color(color.r, color.g, color.b, 0.22f), RuntimeUI.HeartSprite);
            note.glow.rectTransform.localScale = Vector3.one * 1.32f;
            note.heart = RuntimeUI.Image("Heart", root, color, RuntimeUI.HeartSprite);
            Outline outline = RuntimeUI.AddOutline(note.heart, Color.white, new Vector2(3f, -3f));
            outline.useGraphicAlpha = true;

            if (kind == HeartKind.Duet)
            {
                note.p1Ready = CreateReadyDot(root, new Vector2(-23, -29), new Color(0.27f, 0.67f, 1f, 0.35f));
                note.p2Ready = CreateReadyDot(root, new Vector2(23, -29), new Color(1f, 0.30f, 0.42f, 0.35f));
            }
            return note;
        }

        private static Image CreateReadyDot(Transform parent, Vector2 position, Color color)
        {
            RectTransform rect = RuntimeUI.FixedRect("Ready Dot", parent, new Vector2(0.5f, 0.5f), position, new Vector2(18, 18));
            Image dot = rect.gameObject.AddComponent<Image>();
            dot.sprite = RuntimeUI.CircleSprite;
            dot.color = color;
            return dot;
        }

        public void UpdateVisual(float songTime)
        {
            float remaining = TargetTime - songTime;
            if (IsHold && HoldStarted)
            {
                rect.anchoredPosition = new Vector2(laneX, targetY);
                float holdProgress = Mathf.Clamp01((songTime - TargetTime) / Mathf.Max(0.01f, HoldDuration));
                float remainingTailLength = holdTailLength * (1f - holdProgress);
                holdTail.rectTransform.sizeDelta = new Vector2(34f, remainingTailLength);
                holdEnd.rectTransform.anchoredPosition = new Vector2(0f, 42f + remainingTailLength);
            }
            else
            {
                float progress = 1f - remaining / travelTime;
                float y = Mathf.LerpUnclamped(spawnY, targetY, progress);
                rect.anchoredPosition = new Vector2(laneX, y);
            }

            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 7f) * 0.035f;
            rect.localScale = Vector3.one * pulse;
            float near = HoldStarted ? 1f : Mathf.Clamp01(1f - Mathf.Abs(remaining) / 0.45f);
            glow.color = new Color(glow.color.r, glow.color.g, glow.color.b, Mathf.Lerp(0.18f, 0.48f, near));
        }

        public bool CanReceive(int player, float songTime, RhythmSettings settings)
        {
            if (Resolved || HoldStarted || Mathf.Abs(songTime - TargetTime) > settings.GoodWindow)
                return false;
            if (Kind == HeartKind.PlayerOne)
                return player == 1;
            if (Kind == HeartKind.PlayerTwo)
                return player == 2;
            return player == 1 ? !PlayerOneReady : !PlayerTwoReady;
        }

        public bool RegisterPress(int player, float songTime, RhythmSettings settings, out HitRating rating)
        {
            rating = HitRating.None;
            if (!CanReceive(player, songTime, settings))
                return false;

            if (Kind != HeartKind.Duet)
            {
                rating = RhythmJudge.Rate(Mathf.Abs(songTime - TargetTime), settings);
                if (IsHold)
                {
                    HoldStarted = true;
                    holdStartRating = rating;
                    rating = HitRating.None;
                }
                else
                {
                    Resolved = true;
                }
                return true;
            }

            if (player == 1)
            {
                PlayerOneReady = true;
                PlayerOnePressTime = songTime;
                p1Ready.color = new Color(0.27f, 0.74f, 1f, 1f);
            }
            else
            {
                PlayerTwoReady = true;
                PlayerTwoPressTime = songTime;
                p2Ready.color = new Color(1f, 0.30f, 0.46f, 1f);
            }

            if (!PlayerOneReady || !PlayerTwoReady)
                return true;

            float syncError = Mathf.Abs(PlayerOnePressTime - PlayerTwoPressTime);
            float timingError = Mathf.Max(Mathf.Abs(PlayerOnePressTime - TargetTime), Mathf.Abs(PlayerTwoPressTime - TargetTime));
            rating = syncError <= settings.DuetSyncWindow ? RhythmJudge.Rate(timingError, settings) : HitRating.Miss;
            Resolved = true;
            return true;
        }

        public bool TryExpire(float songTime, RhythmSettings settings)
        {
            if (Resolved || HoldStarted || songTime <= TargetTime + settings.GoodWindow)
                return false;
            Resolved = true;
            return true;
        }

        public bool TryCompleteHold(float songTime, bool buttonHeld, RhythmSettings settings, out HitRating rating)
        {
            rating = HitRating.None;
            if (!IsHold || !HoldStarted || Resolved)
                return false;

            if (buttonHeld && songTime < HoldEndTime)
                return false;

            if (buttonHeld)
            {
                rating = holdStartRating;
            }
            else
            {
                float releaseError = Mathf.Abs(songTime - HoldEndTime);
                HitRating releaseRating = RhythmJudge.Rate(releaseError, settings);
                rating = (HitRating)Mathf.Max((int)holdStartRating, (int)releaseRating);
            }

            Resolved = true;
            return true;
        }

        public void ResolveAnimation(HitRating rating)
        {
            Resolved = true;
            StopAllCoroutines();
            StartCoroutine(ResolveRoutine(rating));
        }

        private System.Collections.IEnumerator ResolveRoutine(HitRating rating)
        {
            float duration = rating == HitRating.Miss ? 0.3f : 0.2f;
            Vector3 startScale = rect.localScale;
            Color start = heart.color;
            Color tailStart = holdTail != null ? holdTail.color : Color.clear;
            Color endStart = holdEnd != null ? holdEnd.color : Color.clear;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - (1f - t) * (1f - t);
                rect.localScale = startScale * Mathf.Lerp(1f, rating == HitRating.Miss ? 0.7f : 1.55f, eased);
                heart.color = new Color(start.r, start.g, start.b, 1f - eased);
                glow.color = new Color(glow.color.r, glow.color.g, glow.color.b, (1f - eased) * 0.4f);
                if (holdTail != null)
                    holdTail.color = new Color(tailStart.r, tailStart.g, tailStart.b, tailStart.a * (1f - eased));
                if (holdEnd != null)
                    holdEnd.color = new Color(endStart.r, endStart.g, endStart.b, endStart.a * (1f - eased));
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
