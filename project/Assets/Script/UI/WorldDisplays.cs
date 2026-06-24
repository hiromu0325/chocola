using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 監視モニター（ワールド空間）
    /// ・探索中：次の来訪までのカウントダウン
    /// ・警告中：探索者が近づいてくる映像（シルエットが拡大）＋特徴テキスト
    /// ・来訪中：在室表示
    /// プレイヤーはこの映像で特徴を観察し、手帳で種類を特定して対策する
    /// </summary>
    public class MonitorDisplay : MonoBehaviour
    {
        private TextMesh _text;
        private Renderer _screen;
        private Transform _silhouette;
        private Renderer _silRenderer;
        private AudioSource _audio;
        private SearcherType _next;
        private float _alarmTimer;

        private void Awake()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            var screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screen.name = "Screen";
            screen.transform.SetParent(transform, false);
            screen.transform.localScale = new Vector3(1.6f, 1.0f, 0.05f);
            Object.Destroy(screen.GetComponent<Collider>());
            _screen = screen.GetComponent<Renderer>();
            _screen.material = new Material(shader) { color = new Color(0.02f, 0.05f, 0.03f) };

            // 接近シルエット（暗い人型。警告中に拡大）
            var sil = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            sil.name = "Silhouette";
            sil.transform.SetParent(transform, false);
            sil.transform.localPosition = new Vector3(0.45f, 0f, -0.04f);
            Object.Destroy(sil.GetComponent<Collider>());
            _silhouette = sil.transform;
            _silRenderer = sil.GetComponent<Renderer>();
            _silRenderer.material = new Material(shader) { color = new Color(0.01f, 0.02f, 0.01f) };
            sil.SetActive(false);

            var textGo = new GameObject("MonitorText");
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = new Vector3(-0.74f, 0.42f, -0.04f);
            _text = textGo.AddComponent<TextMesh>();
            _text.font = FontProvider.Get();
            textGo.GetComponent<MeshRenderer>().material = _text.font.material;
            _text.fontSize = 64; _text.characterSize = 0.05f;
            _text.anchor = TextAnchor.UpperLeft;
            _text.color = new Color(0.4f, 1f, 0.5f);

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 1f; _audio.maxDistance = 20f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
        }

        private void OnEnable()
        {
            GameEvents.OnNextSearcherAnnounced += HandleAnnounce;
            GameEvents.OnPhaseChanged += HandlePhase;
        }
        private void OnDisable()
        {
            GameEvents.OnNextSearcherAnnounced -= HandleAnnounce;
            GameEvents.OnPhaseChanged -= HandlePhase;
        }

        private void HandleAnnounce(SearcherType t)
        {
            _next = t;
            _audio.PlayOneShot(ProceduralAudio.Beep(), 0.8f);
        }

        private void HandlePhase(GamePhase phase)
        {
            _silhouette.gameObject.SetActive(phase == GamePhase.Warning);
            if (phase == GamePhase.Warning) _audio.PlayOneShot(ProceduralAudio.Alarm(), 0.9f);
        }

        private void Update()
        {
            var pm = PhaseManager.Instance;
            if (pm == null || _text == null) return;

            switch (pm.CurrentPhase)
            {
                case GamePhase.Exploration:
                    _text.color = new Color(0.4f, 1f, 0.5f);
                    _text.text = $"== 監視カメラ ==\n来訪まで {Fmt(pm.TimeUntilVisit)}\n\n古時計の鐘で\n来訪が始まる";
                    break;

                case GamePhase.Warning:
                {
                    // シルエットが奥から手前へ（1分かけて到達）
                    float k = 1f - Mathf.Clamp01(pm.PhaseRemaining / 60f); // 0→1
                    float scale = Mathf.Lerp(0.15f, 1.4f, k);
                    _silhouette.localScale = new Vector3(scale * 0.5f, scale * 0.7f, scale * 0.5f);
                    _silhouette.localPosition = new Vector3(0.45f, -0.5f + scale * 0.45f, -0.04f);

                    bool on = Mathf.PingPong(Time.time * 3f, 1f) > 0.5f;
                    _text.color = on ? Color.red : new Color(0.5f, 0.05f, 0.05f);
                    _text.text = $"!! 接近中 !!\n到達まで {Fmt(pm.PhaseRemaining)}\n\n【特徴】\n{GameEvents.GetSearcherFeature(_next)}";

                    _alarmTimer -= Time.deltaTime;
                    if (_alarmTimer <= 0f) { _audio.PlayOneShot(ProceduralAudio.Alarm(), 0.6f); _alarmTimer = 3f; }
                    break;
                }

                case GamePhase.Visit:
                    _text.color = new Color(1f, 0.2f, 0.1f);
                    _text.text = $"●REC 在室中\n\n退去まで {Fmt(pm.PhaseRemaining)}\n\n机から離れるな";
                    break;
            }
        }

        public static string Fmt(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }
    }

    /// <summary>
    /// 古時計：針が一周すると鐘が鳴り来訪フェーズへ
    /// 探索フェーズの進行に合わせて長針が一周する。来訪/イベント中は停止。
    /// </summary>
    public class GrandfatherClock : MonoBehaviour
    {
        private Transform _longHand;   // 一周＝探索フェーズ全体
        private Transform _shortHand;  // 飾り（ゆっくり）
        private TextMesh _label;
        private AudioSource _audio;
        private bool _chimedThisCycle;

        private void Awake()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            // 筐体
            var cabinet = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabinet.name = "Cabinet";
            cabinet.transform.SetParent(transform, false);
            cabinet.transform.localPosition = new Vector3(0f, 1.1f, 0.02f);
            cabinet.transform.localScale = new Vector3(0.7f, 2.2f, 0.3f);
            cabinet.GetComponent<Renderer>().material = new Material(shader) { color = new Color(0.25f, 0.16f, 0.1f) };

            // 文字盤
            var face = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            face.name = "Face";
            face.transform.SetParent(transform, false);
            face.transform.localPosition = new Vector3(0f, 1.85f, -0.14f);
            face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            face.transform.localScale = new Vector3(0.5f, 0.03f, 0.5f);
            Object.Destroy(face.GetComponent<Collider>());
            face.GetComponent<Renderer>().material = new Material(shader) { color = new Color(0.92f, 0.88f, 0.78f) };

            _longHand = MakeHand(shader, "LongHand", 0.22f, new Color(0.1f, 0.1f, 0.1f));
            _shortHand = MakeHand(shader, "ShortHand", 0.14f, new Color(0.2f, 0.2f, 0.2f));

            var labelGo = new GameObject("ClockLabel");
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 1.0f, -0.16f);
            _label = labelGo.AddComponent<TextMesh>();
            _label.font = FontProvider.Get();
            labelGo.GetComponent<MeshRenderer>().material = _label.font.material;
            _label.fontSize = 64; _label.characterSize = 0.03f;
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.spatialBlend = 1f; _audio.maxDistance = 25f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
        }

        private Transform MakeHand(Shader shader, string name, float length, Color color)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = new Vector3(0f, 1.85f, -0.17f);

            var hand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hand.transform.SetParent(pivot, false);
            hand.transform.localPosition = new Vector3(0f, length * 0.5f, 0f);
            hand.transform.localScale = new Vector3(0.02f, length, 0.02f);
            Object.Destroy(hand.GetComponent<Collider>());
            hand.GetComponent<Renderer>().material = new Material(shader) { color = color };
            return pivot;
        }

        private void OnEnable() => GameEvents.OnClockChime += Chime;
        private void OnDisable() => GameEvents.OnClockChime -= Chime;

        private void Chime()
        {
            // 鐘を複数回
            for (int i = 0; i < 3; i++)
                Invoke(nameof(PlayBell), 0.45f * i);
        }
        private void PlayBell() => _audio.PlayOneShot(ProceduralAudio.Bell(), 1f);

        private void Update()
        {
            var pm = PhaseManager.Instance;
            if (pm == null || _longHand == null) return;

            if (pm.CurrentPhase == GamePhase.Exploration && !pm.EventActive)
            {
                _chimedThisCycle = false;
                float prog = 1f - Mathf.Clamp01(pm.PhaseRemaining / Mathf.Max(1f, pm.ExplorationDuration));
                _longHand.localRotation = Quaternion.Euler(0f, 0f, -prog * 360f);
                _shortHand.localRotation = Quaternion.Euler(0f, 0f, -prog * 30f);
                _label.color = new Color(0.6f, 1f, 0.7f);
                _label.text = "時を刻む";
            }
            else
            {
                // 来訪/イベント中は停止
                _label.color = new Color(1f, 0.3f, 0.25f);
                _label.text = pm.EventActive ? "止まっている" : "停止";
            }
        }
    }
}
