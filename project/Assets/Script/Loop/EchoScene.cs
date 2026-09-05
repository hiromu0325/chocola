using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 人物残像（残響）。記憶の一場面をシルエットでループ再生する演出。
    /// 【決定事項】主人公との対話は無い。文章では伝わらない情景の補強に使う。
    /// 同じ場面を別人物の記憶として2箇所に置き、パラメータ差分
    /// （揺れの激しさ・声の高さ/大きさ・シルエットの大きさ）で
    /// 視点のすれ違いを表現する（例: 佐伯視点=冷静な対話 / 黒田視点=怒鳴られている）。
    ///
    /// 挙動:
    /// ・普段は薄く佇む（干渉しない。コライダー無し）
    /// ・プレイヤーが近づく(ActivateRadius)と再生開始＝揺れ＋こもった話し声。
    ///   初回再生時に手帳へ「残響」として記録される
    /// ・さらに踏み込む(DissolveRadius)と霧散し、しばらくして再び現れる（何度でも見られる）
    /// </summary>
    public class EchoScene : MonoBehaviour
    {
        [Tooltip("手帳記録用の一意ID")]
        public string EchoId;
        [Tooltip("手帳タイトル（例: 残響：口論（佐伯の記憶））")]
        public string NoteTitle;
        [TextArea] public string NoteBody;

        [Tooltip("再生が始まる距離")]
        public float ActivateRadius = 6f;
        [Tooltip("霧散する距離")]
        public float DissolveRadius = 1.3f;
        [Tooltip("霧散から再出現までの秒数")]
        public float ReappearSeconds = 8f;

        [Tooltip("揺れ角（度）。冷静=2〜3 / 怒鳴り=12〜16")]
        public float SwayAmount = 3f;
        public float SwaySpeed = 1.2f;
        [Tooltip("声の高さ。低いほど威圧的")]
        public float VoicePitch = 1f;
        [Tooltip("声の大きさ。0で無音の場面（遠くから見ただけの記憶）")]
        public float VoiceVolume = 0.35f;

        [Tooltip("シルエット（親Transform）。ビルダーが流し込む")]
        public Transform[] Figures;
        [Tooltip("Figuresと同数。人物ごとの揺れ倍率（0=動かない影）")]
        public float[] FigureSway;

        [Header("台詞（Irodori-TTS生成）。空ならこもった話し声をループ")]
        [Tooltip("会話の台詞を順に再生する（ループ）")]
        public AudioClip[] Lines;
        [Tooltip("Linesと同数。台詞ごとのピッチ（黒い影の声を低くする等）。省略時はVoicePitch")]
        public float[] LinePitch;
        [Tooltip("台詞と台詞の間（秒）")]
        public float LineGap = 0.7f;
        [Tooltip("会話を一巡した後の間（秒）")]
        public float LoopPause = 3.5f;

        private int _lineIdx;
        private float _nextLineAt;
        private bool HasLines => Lines != null && Lines.Length > 0;

        private Transform _player;
        private AudioSource _voice;
        private Renderer[] _renderers;
        private Color[] _baseColors;
        private Vector3[] _basePos;
        private Quaternion[] _baseRot;
        private float[] _phase;
        private MaterialPropertyBlock _mpb;

        private bool _noted;
        private bool _active;
        private float _activeSince;        // 再生開始時刻（直後の霧散を防ぐ猶予に使う）
        private float _fade = 1f;          // 1=表示 0=霧散
        private float _reappearAt = -1f;   // この時刻を過ぎたら再出現

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _baseColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
                _baseColors[i] = _renderers[i].sharedMaterial != null
                    ? _renderers[i].sharedMaterial.color
                    : Color.white;

            int n = Figures != null ? Figures.Length : 0;
            _basePos = new Vector3[n];
            _baseRot = new Quaternion[n];
            _phase = new float[n];
            for (int i = 0; i < n; i++)
            {
                _basePos[i] = Figures[i].localPosition;
                _baseRot[i] = Figures[i].localRotation;
                _phase[i] = (i * 2.1f + 0.7f) % (Mathf.PI * 2f);
            }

            _voice = gameObject.AddComponent<AudioSource>();
            // 台詞があれば1本ずつ順に再生。無ければこもった話し声をループ
            _voice.clip = HasLines ? null : ProceduralAudio.Murmur();
            _voice.loop = !HasLines;
            _voice.playOnAwake = false;
            _voice.spatialBlend = 1f;
            _voice.rolloffMode = AudioRolloffMode.Linear;
            _voice.maxDistance = ActivateRadius + 4f;
            _voice.pitch = VoicePitch;
            _voice.volume = 0f;
        }

        private void Update()
        {
            if (_player == null)
            {
                var go = GameObject.FindGameObjectWithTag("Player");
                if (go == null) return;
                _player = go.transform;
            }

            // 霧散中→再出現待ち
            if (_reappearAt > 0f)
            {
                _fade = Mathf.MoveTowards(_fade, 0f, Time.deltaTime * 1.6f);
                ApplyFade();
                if (_voice.isPlaying)
                    _voice.volume = Mathf.MoveTowards(_voice.volume, 0f, Time.deltaTime * 1.5f);
                if (_fade <= 0f && Time.time >= _reappearAt)
                {
                    _reappearAt = -1f;
                    _active = false;
                    _voice.Stop();
                    _lineIdx = 0;
                }
                return;
            }

            _fade = Mathf.MoveTowards(_fade, 1f, Time.deltaTime * 0.8f);
            ApplyFade();

            float d = Vector3.Distance(_player.position, transform.position);

            // 踏み込み過ぎ＝霧散（記憶に触れることはできない）。
            // 部屋のスポーンが近い場合に入室した瞬間消えないよう、再生開始直後は猶予を置く
            if (d < DissolveRadius && _active && Time.time - _activeSince > 1.0f)
            {
                _reappearAt = Time.time + ReappearSeconds;
                ProceduralAudio.PlayAt(ProceduralAudio.Bell(), transform.position, 0.25f);
                return;
            }

            bool near = d < ActivateRadius;
            if (near && !_active)
            {
                _active = true;
                _activeSince = Time.time;
                _lineIdx = 0;
                _nextLineAt = Time.time + 0.6f;   // 姿が動き出してから話し始める
                if (!HasLines && VoiceVolume > 0.001f && !_voice.isPlaying) _voice.Play();
                if (!_noted && !string.IsNullOrEmpty(NoteTitle))
                {
                    _noted = true;
                    if (Notebook.Add("echo_" + EchoId, NoteTitle, NoteBody))
                        ToastUI.Show($"残響を見た──『{NoteTitle}』を手帳に記録した");
                }
            }
            else if (!near && _active)
            {
                _active = false;
                if (HasLines) _voice.Stop();
            }

            if (HasLines)
            {
                // 台詞を順に再生（間を空けて一巡したら少し休んで繰り返す）
                _voice.volume = VoiceVolume * _fade;
                if (_active && VoiceVolume > 0.001f && !_voice.isPlaying && Time.time >= _nextLineAt)
                {
                    var clip = Lines[_lineIdx];
                    if (clip != null)
                    {
                        _voice.clip = clip;
                        _voice.pitch = (LinePitch != null && _lineIdx < LinePitch.Length && LinePitch[_lineIdx] > 0f)
                            ? LinePitch[_lineIdx] : VoicePitch;
                        _voice.Play();
                        _nextLineAt = Time.time + clip.length / Mathf.Max(0.1f, _voice.pitch) + LineGap;
                    }
                    else _nextLineAt = Time.time + LineGap;
                    _lineIdx++;
                    if (_lineIdx >= Lines.Length) { _lineIdx = 0; _nextLineAt += LoopPause; }
                }
            }
            else
            {
                // こもった話し声は接近でフェードイン
                float targetVol = (_active && VoiceVolume > 0.001f) ? VoiceVolume : 0f;
                _voice.volume = Mathf.MoveTowards(_voice.volume, targetVol, Time.deltaTime * 0.8f);
                if (_voice.volume <= 0.001f && !_active && _voice.isPlaying) _voice.Stop();
            }

            // 再生中だけシルエットが揺れる（記憶が動き出す）
            if (Figures == null) return;
            for (int i = 0; i < Figures.Length; i++)
            {
                if (Figures[i] == null) continue;
                float mul = (FigureSway != null && i < FigureSway.Length) ? FigureSway[i] : 1f;
                if (!_active || mul <= 0f)
                {
                    Figures[i].localPosition = _basePos[i];
                    Figures[i].localRotation = _baseRot[i];
                    continue;
                }
                float w = Time.time * SwaySpeed + _phase[i];
                float lean = Mathf.Sin(w) * SwayAmount * mul;
                float bob = Mathf.Sin(w * 0.7f) * 0.02f * mul;
                Figures[i].localPosition = _basePos[i] + new Vector3(0f, bob, 0f);
                Figures[i].localRotation = _baseRot[i] * Quaternion.Euler(0f, 0f, lean);
            }
        }

        /// <summary>シルエット全体の透明度を現在のフェード値で更新</summary>
        private void ApplyFade()
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                var c = _baseColors[i];
                c.a *= _fade;
                _mpb.SetColor(BaseColorId, c);
                _renderers[i].SetPropertyBlock(_mpb);
            }
        }
    }
}
