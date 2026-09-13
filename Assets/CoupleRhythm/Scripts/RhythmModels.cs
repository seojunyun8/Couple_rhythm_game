using System;
using UnityEngine;

namespace CoupleRhythm
{
    public enum HeartKind
    {
        PlayerOne,
        PlayerTwo,
        Duet
    }

    public enum HitRating
    {
        None,
        Perfect,
        Good,
        Miss
    }

    [Serializable]
    public sealed class SongDefinition
    {
        public string id;
        public string title;
        public string artist;
        public string resourceName;
        public float defaultBpm;
        public float highlightStartSeconds;
        public float highlightEndSeconds;
        public Color accent;

        public SongDefinition(string id, string title, string artist, string resourceName, float defaultBpm, float highlightStartSeconds, float highlightEndSeconds, Color accent)
        {
            this.id = id;
            this.title = title;
            this.artist = artist;
            this.resourceName = resourceName;
            this.defaultBpm = defaultBpm;
            this.highlightStartSeconds = highlightStartSeconds;
            this.highlightEndSeconds = highlightEndSeconds;
            this.accent = accent;
        }
    }

    public sealed class RhythmSettings
    {
        private const string Prefix = "CoupleRhythm.";

        public float PerfectWindowMs = 100f;
        public float GoodWindowMs = 190f;
        public float DuetSyncWindowMs = 130f;
        public float AudioOffsetMs;
        public int HeartsPerBeat = 1;
        public readonly float[] SongBpms = new float[4];

        public float PerfectWindow => PerfectWindowMs / 1000f;
        public float GoodWindow => GoodWindowMs / 1000f;
        public float DuetSyncWindow => DuetSyncWindowMs / 1000f;
        public float AudioOffset => AudioOffsetMs / 1000f;

        public static RhythmSettings Load(SongDefinition[] songs)
        {
            RhythmSettings settings = new RhythmSettings
            {
                PerfectWindowMs = PlayerPrefs.GetFloat(Prefix + "PerfectMs", 100f),
                GoodWindowMs = PlayerPrefs.GetFloat(Prefix + "GoodMs", 190f),
                DuetSyncWindowMs = PlayerPrefs.GetFloat(Prefix + "DuetMs", 130f),
                AudioOffsetMs = PlayerPrefs.GetFloat(Prefix + "OffsetMs", 0f),
                HeartsPerBeat = PlayerPrefs.GetInt(Prefix + "HeartsPerBeat", 1)
            };

            for (int i = 0; i < settings.SongBpms.Length; i++)
            {
                float fallback = songs != null && i < songs.Length ? songs[i].defaultBpm : 120f;
                settings.SongBpms[i] = PlayerPrefs.GetFloat(Prefix + "Bpm" + i, fallback);
            }

            settings.Clamp();
            return settings;
        }

        public void Save()
        {
            Clamp();
            PlayerPrefs.SetFloat(Prefix + "PerfectMs", PerfectWindowMs);
            PlayerPrefs.SetFloat(Prefix + "GoodMs", GoodWindowMs);
            PlayerPrefs.SetFloat(Prefix + "DuetMs", DuetSyncWindowMs);
            PlayerPrefs.SetFloat(Prefix + "OffsetMs", AudioOffsetMs);
            PlayerPrefs.SetInt(Prefix + "HeartsPerBeat", HeartsPerBeat);
            for (int i = 0; i < SongBpms.Length; i++)
                PlayerPrefs.SetFloat(Prefix + "Bpm" + i, SongBpms[i]);
            PlayerPrefs.Save();
        }

        public void ResetToDefaults(SongDefinition[] songs)
        {
            PerfectWindowMs = 100f;
            GoodWindowMs = 190f;
            DuetSyncWindowMs = 130f;
            AudioOffsetMs = 0f;
            HeartsPerBeat = 1;
            for (int i = 0; i < SongBpms.Length; i++)
                SongBpms[i] = songs != null && i < songs.Length ? songs[i].defaultBpm : 120f;
            Save();
        }

        public void Clamp()
        {
            PerfectWindowMs = Mathf.Clamp(PerfectWindowMs, 35f, 250f);
            GoodWindowMs = Mathf.Clamp(GoodWindowMs, PerfectWindowMs + 20f, 400f);
            DuetSyncWindowMs = Mathf.Clamp(DuetSyncWindowMs, 35f, 300f);
            AudioOffsetMs = Mathf.Clamp(AudioOffsetMs, -300f, 300f);
            HeartsPerBeat = Mathf.Clamp(HeartsPerBeat, 1, 4);
            for (int i = 0; i < SongBpms.Length; i++)
                SongBpms[i] = Mathf.Clamp(SongBpms[i], 60f, 220f);
        }
    }

    public static class RhythmJudge
    {
        public static HitRating Rate(float absoluteTimingError, RhythmSettings settings)
        {
            if (absoluteTimingError <= settings.PerfectWindow)
                return HitRating.Perfect;
            if (absoluteTimingError <= settings.GoodWindow)
                return HitRating.Good;
            return HitRating.Miss;
        }

    }
}
