using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 日本語表示可能なフォントの取得（OSフォント→ビルトインの順でフォールバック）
    /// ※ ビルトインの LegacyRuntime.ttf は日本語グリフを持たないため
    /// </summary>
    public static class FontProvider
    {
        private static Font _cached;

        private static readonly string[] Candidates =
        {
            "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo",
            "MS UI Gothic", "MS Gothic",
            "Hiragino Sans", "Hiragino Kaku Gothic ProN",
            "Noto Sans CJK JP", "Noto Sans JP"
        };

        public static Font Get()
        {
            if (_cached != null) return _cached;

            var installed = Font.GetOSInstalledFontNames();
            foreach (var name in Candidates)
            {
                foreach (var os in installed)
                {
                    if (os == name)
                    {
                        _cached = Font.CreateDynamicFontFromOSFont(name, 32);
                        if (_cached != null) return _cached;
                    }
                }
            }
            // フォールバック（日本語は豆腐になるが動作はする）
            _cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _cached;
        }
    }

    /// <summary>
    /// アセット不要のプロシージャル効果音生成
    /// </summary>
    public static class ProceduralAudio
    {
        private const int SampleRate = 44100;

        private static AudioClip _scream, _beep, _alarm, _footstep, _click, _unlock;

        /// <summary>ジャンプスケア用の悲鳴っぽいノイズ</summary>
        public static AudioClip Scream()
        {
            if (_scream != null) return _scream;
            _scream = Generate("scream", 0.9f, (t, dur) =>
            {
                float env = Mathf.Exp(-3.5f * t / dur);
                float freq = Mathf.Lerp(1400f, 350f, t / dur);
                float tone = Mathf.Sin(2f * Mathf.PI * freq * t + 6f * Mathf.Sin(2f * Mathf.PI * 31f * t));
                float noise = (Random.value * 2f - 1f) * 0.8f;
                return (tone * 0.55f + noise * 0.45f) * env;
            });
            return _scream;
        }

        /// <summary>モニター通知ビープ</summary>
        public static AudioClip Beep()
        {
            if (_beep != null) return _beep;
            _beep = Generate("beep", 0.15f, (t, dur) =>
                Mathf.Sin(2f * Mathf.PI * 880f * t) * Mathf.Exp(-8f * t / dur) * 0.5f);
            return _beep;
        }

        /// <summary>警告アラーム（2音サイレン）</summary>
        public static AudioClip Alarm()
        {
            if (_alarm != null) return _alarm;
            _alarm = Generate("alarm", 0.8f, (t, dur) =>
            {
                float freq = (t % 0.4f) < 0.2f ? 760f : 580f;
                return Mathf.Sin(2f * Mathf.PI * freq * t) * 0.35f;
            });
            return _alarm;
        }

        /// <summary>敵の足音（低い打撃音）</summary>
        public static AudioClip Footstep()
        {
            if (_footstep != null) return _footstep;
            _footstep = Generate("footstep", 0.22f, (t, dur) =>
            {
                float env = Mathf.Exp(-22f * t);
                float thump = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(95f, 45f, t / dur) * t);
                float noise = (Random.value * 2f - 1f) * Mathf.Exp(-40f * t) * 0.4f;
                return (thump + noise) * env * 0.9f;
            });
            return _footstep;
        }

        /// <summary>ギミック操作のカチカチ音</summary>
        public static AudioClip Click()
        {
            if (_click != null) return _click;
            _click = Generate("click", 0.05f, (t, dur) =>
                (Random.value * 2f - 1f) * Mathf.Exp(-90f * t) * 0.5f);
            return _click;
        }

        /// <summary>ギミック解除成功音</summary>
        public static AudioClip Unlock()
        {
            if (_unlock != null) return _unlock;
            _unlock = Generate("unlock", 0.5f, (t, dur) =>
            {
                float f = t < 0.18f ? 523f : (t < 0.34f ? 659f : 784f);
                return Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-4f * t / dur) * 0.4f;
            });
            return _unlock;
        }

        private delegate float SampleFunc(float time, float duration);

        private static AudioClip Generate(string name, float duration, SampleFunc func)
        {
            int count = Mathf.CeilToInt(SampleRate * duration);
            var data = new float[count];
            for (int i = 0; i < count; i++)
                data[i] = Mathf.Clamp(func(i / (float)SampleRate, duration), -1f, 1f);

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>その場限りの AudioSource で再生（3D/2D 切替可）</summary>
        public static void PlayAt(AudioClip clip, Vector3 pos, float volume = 1f, bool spatial = true)
        {
            if (clip == null) return;
            var go = new GameObject("OneShotAudio");
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.spatialBlend = spatial ? 1f : 0f;
            src.maxDistance = 25f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }
    }
}
