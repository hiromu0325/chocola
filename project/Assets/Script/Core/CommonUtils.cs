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

        private static AudioClip _scream, _beep, _alarm, _footstep, _click, _unlock, _tension;

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

        /// <summary>
        /// 襲撃中の不安を煽るBGM（4秒ループ）。
        /// 半音でうなる低音ドローン＋遅い脈動＋かすかな高音＋毎秒の心音。
        /// 全成分の周期が4秒の約数になるよう選んであり、切れ目なくループする。
        /// </summary>
        public static AudioClip TensionLoop()
        {
            if (_tension != null) return _tension;
            _tension = Generate("tension", 4.0f, (t, dur) =>
            {
                // 低音ドローン2声（55Hzと58.25Hz。約3Hzのうなりが不協和を作る）
                float drone = (Mathf.Sin(2f * Mathf.PI * 55f * t)
                             + Mathf.Sin(2f * Mathf.PI * 58.25f * t)) * 0.27f;
                // ゆっくりした脈動（呼吸のような強弱）
                float throb = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 1.25f * t - Mathf.PI * 0.5f);
                // かすかに揺れる高い倍音（耳の後ろがざわつく成分）
                float high = Mathf.Sin(2f * Mathf.PI * 466.25f * t)
                           * (0.045f + 0.045f * Mathf.Sin(2f * Mathf.PI * 0.75f * t));
                // 毎秒の心音（周期1秒＝ループ長の約数なので境界が繋がる）
                float tb = t % 1.0f;
                float thump = Mathf.Sin(2f * Mathf.PI * 48f * tb) * Mathf.Exp(-9f * tb) * 0.5f;
                return (drone * throb + high + thump) * 0.55f;
            });
            return _tension;
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

        private static AudioClip _bell, _laugh;

        /// <summary>探索者の鈴の音（澄んだ金属音）</summary>
        public static AudioClip Bell()
        {
            if (_bell != null) return _bell;
            _bell = Generate("bell", 0.6f, (t, dur) =>
            {
                float env = Mathf.Exp(-5f * t / dur);
                // 倍音を重ねた鈴
                float s = Mathf.Sin(2f * Mathf.PI * 2100f * t) * 0.5f
                        + Mathf.Sin(2f * Mathf.PI * 3150f * t) * 0.3f
                        + Mathf.Sin(2f * Mathf.PI * 4480f * t) * 0.2f;
                return s * env * 0.4f;
            });
            return _bell;
        }

        /// <summary>女の子の笑い声（イベント開始合図）</summary>
        public static AudioClip Laugh()
        {
            if (_laugh != null) return _laugh;
            _laugh = Generate("laugh", 1.6f, (t, dur) =>
            {
                // 「ふふふ」を模した断続的なフォルマント
                float syl = Mathf.Repeat(t, 0.32f);
                float gate = syl < 0.16f ? 1f : 0f;
                float pitch = 620f + Mathf.Sin(2f * Mathf.PI * 5f * t) * 60f;
                float vib = Mathf.Sin(2f * Mathf.PI * pitch * t)
                          + 0.5f * Mathf.Sin(2f * Mathf.PI * pitch * 2f * t);
                float env = Mathf.Exp(-1.0f * t / dur);
                return vib * gate * env * 0.3f;
            });
            return _laugh;
        }

        private static AudioClip _murmur;

        /// <summary>
        /// 残響（人物残像）のこもった話し声（3.2秒ループ）。
        /// 言葉として聞き取れない抑揚だけの会話。EchoSceneがpitch/volumeを変えて
        /// 「冷静な対話」と「怒鳴り声」を同じクリップから作り分ける。
        /// </summary>
        public static AudioClip Murmur()
        {
            if (_murmur != null) return _murmur;
            _murmur = Generate("murmur", 3.2f, (t, dur) =>
            {
                // 音節ゲート（周期0.4秒＝ループ長の約数。話す/黙るの繰り返し）
                float syl = Mathf.Repeat(t, 0.4f);
                float gate = syl < 0.22f ? 1f : 0.12f;
                // 低いフォルマント（壁越しに聞こえる声の芯）＋ゆっくりした抑揚
                float f0 = 165f + 45f * Mathf.Sin(2f * Mathf.PI * 0.625f * t);
                float v = Mathf.Sin(2f * Mathf.PI * f0 * t) * 0.55f
                        + Mathf.Sin(2f * Mathf.PI * f0 * 2.15f * t) * 0.25f
                        + (Random.value * 2f - 1f) * 0.10f;
                return v * gate * 0.30f;
            });
            return _murmur;
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

        private static AudioClip _dialTick, _dialBuzz;
        private static readonly AudioClip[] _dialSpecial = new AudioClip[3];

        /// <summary>ダイヤルを1目盛り回した時の通常の「カチッ」</summary>
        public static AudioClip DialTick()
        {
            if (_dialTick != null) return _dialTick;
            _dialTick = Generate("dialtick", 0.04f, (t, dur) =>
                (Random.value * 2f - 1f) * Mathf.Exp(-120f * t) * 0.45f);
            return _dialTick;
        }

        /// <summary>暗証桁の位置だけ鳴る特別音。order(0/1/2)で音程を上げ、順番を示す</summary>
        public static AudioClip DialSpecial(int order)
        {
            order = Mathf.Clamp(order, 0, 2);
            if (_dialSpecial[order] != null) return _dialSpecial[order];
            float baseFreq = new[] { 440f, 620f, 880f }[order]; // 低→中→高＝1桁目→3桁目
            _dialSpecial[order] = Generate($"dialsp{order}", 0.5f, (t, dur) =>
            {
                float env = Mathf.Exp(-4.5f * t / dur);
                float s = Mathf.Sin(2f * Mathf.PI * baseFreq * t)
                        + 0.4f * Mathf.Sin(2f * Mathf.PI * baseFreq * 2f * t);
                return s * env * 0.32f;
            });
            return _dialSpecial[order];
        }

        /// <summary>不正解のブザー</summary>
        public static AudioClip DialBuzz()
        {
            if (_dialBuzz != null) return _dialBuzz;
            _dialBuzz = Generate("dialbuzz", 0.4f, (t, dur) =>
            {
                float sq = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 120f * t));
                return sq * Mathf.Exp(-2.5f * t / dur) * 0.3f;
            });
            return _dialBuzz;
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
