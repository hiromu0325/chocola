using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 来訪通知モニター（ワールド空間）
    /// ・次に来る敵のタイプと特徴
    /// ・来訪までのカウントダウン
    /// ・警告フェーズで点滅＋アラーム、来訪中は「在室」表示
    /// </summary>
    public class MonitorDisplay : MonoBehaviour
    {
        [SerializeField] private float _textSize = 0.06f;

        private TextMesh _text;
        private Renderer _screenRenderer;
        private AudioSource _audio;
        private EnemyType _nextType;
        private float _alarmTimer;

        private void Awake()
        {
            // スクリーン板
            var screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screen.name = "Screen";
            screen.transform.SetParent(transform, false);
            screen.transform.localScale = new Vector3(1.6f, 1.0f, 0.05f);
            Object.Destroy(screen.GetComponent<Collider>());
            _screenRenderer = screen.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            _screenRenderer.material = new Material(shader) { color = new Color(0.02f, 0.05f, 0.03f) };

            // テキスト
            var textGo = new GameObject("MonitorText");
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = new Vector3(-0.72f, 0.4f, -0.04f);
            _text = textGo.AddComponent<TextMesh>();
            _text.font = FontProvider.Get();
            textGo.GetComponent<MeshRenderer>().material = _text.font.material;
            _text.fontSize = 64;
            _text.characterSize = _textSize;
            _text.anchor = TextAnchor.UpperLeft;
            _text.color = new Color(0.4f, 1f, 0.5f);

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 1f;
            _audio.maxDistance = 20f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
        }

        private void OnEnable()
        {
            GameEvents.OnNextEnemyAnnounced += HandleAnnounce;
            GameEvents.OnPhaseChanged += HandlePhase;
        }

        private void OnDisable()
        {
            GameEvents.OnNextEnemyAnnounced -= HandleAnnounce;
            GameEvents.OnPhaseChanged -= HandlePhase;
        }

        private void HandleAnnounce(EnemyType type)
        {
            _nextType = type;
            _audio.PlayOneShot(ProceduralAudio.Beep(), 0.8f);
        }

        private void HandlePhase(GamePhase phase)
        {
            if (phase == GamePhase.Warning)
                _audio.PlayOneShot(ProceduralAudio.Alarm(), 0.9f);
        }

        private void Update()
        {
            var pm = PhaseManager.Instance;
            if (pm == null || _text == null) return;

            switch (pm.CurrentPhase)
            {
                case GamePhase.Exploration:
                    _text.color = new Color(0.4f, 1f, 0.5f);
                    _text.text = $"== 監視モニター ==\n来訪まで {FormatTime(pm.TimeUntilVisit)}\n\n" +
                                 $"次の来訪者:\n{GameEvents.GetEnemyName(_nextType)}";
                    break;

                case GamePhase.Warning:
                    // 点滅
                    bool on = Mathf.PingPong(Time.time * 3f, 1f) > 0.5f;
                    _text.color = on ? Color.red : new Color(0.4f, 0.05f, 0.05f);
                    _text.text = $"!! 警告 !!\nまもなく来訪 {FormatTime(pm.PhaseRemaining)}\n\n" +
                                 $"{GameEvents.GetEnemyName(_nextType)}\nただちに隠れろ";
                    // 警告中はアラームを繰り返す
                    _alarmTimer -= Time.deltaTime;
                    if (_alarmTimer <= 0f)
                    {
                        _audio.PlayOneShot(ProceduralAudio.Alarm(), 0.7f);
                        _alarmTimer = 2.5f;
                    }
                    break;

                case GamePhase.Visit:
                    _text.color = new Color(1f, 0.2f, 0.1f);
                    _text.text = $"●REC  在室中\n{GameEvents.GetEnemyName(_nextType)}\n\n" +
                                 $"退去まで {FormatTime(pm.PhaseRemaining)}";
                    break;
            }
        }

        public static string FormatTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }
    }

    /// <summary>
    /// 壁掛け時計（ワールド空間）：現在フェーズと残り時間を表示
    /// 「探索フェーズと隠れるフェーズが分かれている」ことを部屋の中で常に確認できる
    /// </summary>
    public class WallClock : MonoBehaviour
    {
        private TextMesh _text;

        private void Awake()
        {
            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            frame.transform.SetParent(transform, false);
            frame.transform.localScale = new Vector3(1.1f, 0.55f, 0.04f);
            Object.Destroy(frame.GetComponent<Collider>());
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            frame.GetComponent<Renderer>().material = new Material(shader) { color = new Color(0.1f, 0.1f, 0.12f) };

            var textGo = new GameObject("ClockText");
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.03f);
            _text = textGo.AddComponent<TextMesh>();
            _text.font = FontProvider.Get();
            textGo.GetComponent<MeshRenderer>().material = _text.font.material;
            _text.fontSize = 64;
            _text.characterSize = 0.05f;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
        }

        private void Update()
        {
            var pm = PhaseManager.Instance;
            if (pm == null || _text == null) return;

            switch (pm.CurrentPhase)
            {
                case GamePhase.Exploration:
                    _text.color = new Color(0.5f, 1f, 0.6f);
                    _text.text = $"探索フェーズ\n{MonitorDisplay.FormatTime(pm.PhaseRemaining)}";
                    break;
                case GamePhase.Warning:
                    _text.color = new Color(1f, 0.8f, 0.2f);
                    _text.text = $"警告\n{MonitorDisplay.FormatTime(pm.PhaseRemaining)}";
                    break;
                case GamePhase.Visit:
                    _text.color = new Color(1f, 0.25f, 0.2f);
                    _text.text = $"来訪フェーズ\n{MonitorDisplay.FormatTime(pm.PhaseRemaining)}";
                    break;
            }
        }
    }
}
