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
            if (phase == GamePhase.Warning)
            {
                _audio.PlayOneShot(ProceduralAudio.Alarm(), 0.9f);
                Notebook.Add("searcher_" + _next,
                    "探索者: " + GameEvents.GetSearcherName(_next),
                    GameEvents.GetSearcherFeature(_next) + "\n" + GameEvents.GetSearcherCounter(_next));
            }
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
}
