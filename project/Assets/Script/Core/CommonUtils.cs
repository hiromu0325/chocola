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

        private static AudioClip _staticHiss;

        /// <summary>
        /// 砂嵐の音（2秒ループ）。高域寄りのザーッというノイズ＋電源のうなり＋ときどきのプチッ。
        /// 変調の周期は2秒の約数にしてあり、切れ目なくループする。
        /// </summary>
        public static AudioClip StaticHiss()
        {
            if (_staticHiss != null) return _staticHiss;
            float prevIn = 0f, hp = 0f;
            _staticHiss = Generate("statichiss", 2.0f, (t, dur) =>
            {
                float w = Random.value * 2f - 1f;
                hp = 0.93f * (hp + w - prevIn);     // 低音を落として「ザー」に寄せる
                prevIn = w;
                float mod = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 1.5f * t);
                float hum = Mathf.Sin(2f * Mathf.PI * 50f * t) * 0.035f
                          + Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.02f;
                float crackle = Random.value < 0.0006f ? (Random.value * 2f - 1f) * 0.7f : 0f;
                return hp * 0.32f * mod + hum + crackle;
            });
            return _staticHiss;
        }

        private static AudioClip _doorLatch, _doorCreak, _doorShut, _doorRattle;

        /// <summary>金属の短い「カチャ」（t0 秒から）。ノブの空転とラッチが受けから外れる音</summary>
        private static float LatchClick(float t, float t0, float gain)
        {
            float u = t - t0;
            if (u < 0f) return 0f;
            float ping = Mathf.Sin(2f * Mathf.PI * 2350f * u) * 0.6f + Mathf.Sin(2f * Mathf.PI * 3900f * u) * 0.4f;
            return ((Random.value * 2f - 1f) * Mathf.Exp(-160f * u) + ping * Mathf.Exp(-60f * u) * 0.5f) * gain;
        }

        /// <summary>扉のラッチを外す音（ノブを回す→ラッチが抜ける）</summary>
        public static AudioClip DoorLatch()
        {
            if (_doorLatch != null) return _doorLatch;
            _doorLatch = Generate("doorlatch", 0.25f, (t, dur) =>
                LatchClick(t, 0f, 0.35f) + LatchClick(t, 0.075f, 0.55f));
            return _doorLatch;
        }

        private static AudioClip _tapeButton;

        /// <summary>カセットデッキのボタン（再生・停止の「ガチャッ」）。低めの打撃＋ばねの2回のカチ</summary>
        public static AudioClip TapeButton()
        {
            if (_tapeButton != null) return _tapeButton;
            _tapeButton = Generate("tapebutton", 0.22f, (t, dur) =>
            {
                float thump = Mathf.Sin(2f * Mathf.PI * 140f * t) * Mathf.Exp(-45f * t) * 0.35f;
                return thump + LatchClick(t, 0f, 0.3f) * 0.6f + LatchClick(t, 0.045f, 0.4f) * 0.5f;
            });
            return _tapeButton;
        }

        /// <summary>
        /// 蝶番のきしみ（約1秒）。張り付いては滑る摩擦の連打を、木の扉の響き（共振）に通す。
        /// 回る速さに合わせて音程が上がって下がる
        /// </summary>
        public static AudioClip DoorCreak()
        {
            if (_doorCreak != null) return _doorCreak;
            float phase = 0f, y1 = 0f, y2 = 0f, z1 = 0f, z2 = 0f;
            const float r = 0.994f;
            float c1 = 2f * r * Mathf.Cos(2f * Mathf.PI * 820f / SampleRate);
            float c2 = 2f * r * Mathf.Cos(2f * Mathf.PI * 1640f / SampleRate);
            _doorCreak = Generate("doorcreak", 1.05f, (t, dur) =>
            {
                float u = t / dur;
                float f = 120f + 260f * Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.8f)) + 25f * Mathf.Sin(2f * Mathf.PI * 7f * t);
                phase += f / SampleRate;
                float x = 0f;
                if (phase >= 1f) { phase -= 1f; x = 0.6f + Random.value * 0.4f; }
                x += (Random.value * 2f - 1f) * 0.02f;
                float y = x + c1 * y1 - r * r * y2; y2 = y1; y1 = y;
                float z = x + c2 * z1 - r * r * z2; z2 = z1; z1 = z;
                float env = Mathf.Clamp01(u * 8f) * Mathf.Clamp01((1f - u) * 5f);
                return (y * 0.035f + z * 0.02f) * env;
            });
            return _doorCreak;
        }

        /// <summary>扉が閉まる音（木の「ドン」＋ラッチが受けに収まる「カチャン」）</summary>
        public static AudioClip DoorShut()
        {
            if (_doorShut != null) return _doorShut;
            _doorShut = Generate("doorshut", 0.5f, (t, dur) =>
            {
                float thump = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(90f, 48f, t / 0.25f) * t) * Mathf.Exp(-16f * t) * 0.8f;
                float body = (Random.value * 2f - 1f) * Mathf.Exp(-45f * t) * 0.35f;
                return thump + body + LatchClick(t, 0.018f, 0.5f);
            });
            return _doorShut;
        }

        /// <summary>鍵の掛かった扉を揺する音（ラッチが受けに当たる「ガチャガチャ」）</summary>
        public static AudioClip DoorRattle()
        {
            if (_doorRattle != null) return _doorRattle;
            _doorRattle = Generate("doorrattle", 0.4f, (t, dur) =>
            {
                float s = 0f;
                for (int i = 0; i < 3; i++)
                {
                    float t0 = i * 0.095f;
                    s += LatchClick(t, t0, 0.45f);
                    float u = t - t0;
                    if (u >= 0f) s += Mathf.Sin(2f * Mathf.PI * 120f * u) * Mathf.Exp(-40f * u) * 0.3f;
                }
                return s;
            });
            return _doorRattle;
        }

        private static AudioClip _powerSurge;

        /// <summary>
        /// 停電の予兆（約3.4秒）。蛍光灯の安定器のようなブーンという唸りが強まり、
        /// バチバチと火花が混じって途切れ途切れになり、最後にブレイカーが落ちる「ガチン」で消える
        /// </summary>
        public static AudioClip PowerSurge()
        {
            if (_powerSurge != null) return _powerSurge;
            const float trip = 3.18f;
            float gate = 1f, gateTimer = 0f;
            _powerSurge = Generate("powersurge", 3.4f, (t, dur) =>
            {
                if (t >= trip)
                {
                    float u = t - trip;   // ブレイカーが落ちる音（重い打撃＋金属音）
                    return Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(80f, 40f, u / 0.2f) * u) * Mathf.Exp(-14f * u) * 0.9f
                         + LatchClick(t, trip, 0.7f);
                }
                float k = t / trip;
                // 途切れ（照明のちらつきに合わせて、終わりに近いほど頻繁に落ちる）
                gateTimer -= 1f / SampleRate;
                if (gateTimer <= 0f)
                {
                    gateTimer = Random.Range(0.03f, 0.12f);
                    gate = Random.value < Mathf.Lerp(0.08f, 0.55f, k) ? 0.15f : 1f;
                }
                float ph = t * 50f;
                float buzz = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * ph)) * 0.25f
                           + Mathf.Sin(2f * Mathf.PI * ph * 2f) * 0.35f
                           + Mathf.Sin(2f * Mathf.PI * ph * 3f) * 0.15f;
                float crackle = Random.value < Mathf.Lerp(0.0004f, 0.004f, k) ? (Random.value * 2f - 1f) : 0f;
                float hiss = (Random.value * 2f - 1f) * 0.05f * k;
                float env = Mathf.Lerp(0.15f, 0.55f, k * k) * Mathf.Clamp01(t * 6f);
                return (buzz * gate + hiss) * env + crackle * 0.6f;
            });
            return _powerSurge;
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
