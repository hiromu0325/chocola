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
        private Transform _visualRoot;      // 来訪者の実際の見た目（ミニチュア表示）
        private AudioSource _audio;
        private SearcherType _next;
        private SearcherType _builtVisual = (SearcherType)(-1);
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

            // 来訪者の実際の見た目（警告中にミニチュアで表示・接近につれて拡大）。
            // プレイヤーは文字ではなくこの姿を見て、どの探索者が来るか判断する
            var vr = new GameObject("VisitorVisual");
            vr.transform.SetParent(transform, false);
            vr.transform.localPosition = new Vector3(0.35f, -0.5f, -0.06f);
            _visualRoot = vr.transform;
            vr.SetActive(false);

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
            _visualRoot.gameObject.SetActive(phase == GamePhase.Warning);
            if (phase == GamePhase.Warning)
            {
                _audio.PlayOneShot(ProceduralAudio.Alarm(), 0.9f);
                RebuildVisitorVisual();
                Notebook.Add("searcher_" + _next,
                    "探索者: " + GameEvents.GetSearcherName(_next),
                    GameEvents.GetSearcherFeature(_next) + "\n" + GameEvents.GetSearcherCounter(_next));
            }
        }

        /// <summary>映像に映す来訪者の見た目を、実際の探索者と同じ構築処理で作る</summary>
        private void RebuildVisitorVisual()
        {
            if (_builtVisual == _next) return;
            for (int i = _visualRoot.childCount - 1; i >= 0; i--)
                Destroy(_visualRoot.GetChild(i).gameObject);
            var body = new GameObject("Visual");
            body.transform.SetParent(_visualRoot, false);
            EnemySpawner.BuildVisualInto(body, _next);
            // ミニチュアなのでコライダーとライトは除去（電灯システムや当たりに影響させない）
            foreach (var col in body.GetComponentsInChildren<Collider>()) Destroy(col);
            foreach (var l in body.GetComponentsInChildren<Light>()) l.range = 0.6f;
            _builtVisual = _next;
        }

        private void Update()
        {
            var pm = PhaseManager.Instance;
            if (pm == null || _text == null) return;

            switch (pm.CurrentPhase)
            {
                case GamePhase.Exploration:
                    _text.color = new Color(0.4f, 1f, 0.5f);
                    _text.text = "== 監視カメラ ==\n\n異常なし";
                    break;

                case GamePhase.Warning:
                {
                    // 来訪者の姿が奥から手前へ近づいてくる（文字での特徴説明はしない。姿で判断）
                    float k = 1f - Mathf.Clamp01(pm.PhaseRemaining / 60f); // 0→1
                    float scale = Mathf.Lerp(0.12f, 0.5f, k);
                    _visualRoot.localScale = Vector3.one * scale;
                    _visualRoot.localPosition = new Vector3(Mathf.Lerp(0.55f, 0.2f, k), -0.5f, -0.06f);

                    bool on = Mathf.PingPong(Time.time * 3f, 1f) > 0.5f;
                    _text.color = on ? Color.red : new Color(0.5f, 0.05f, 0.05f);
                    _text.text = "●REC";

                    _alarmTimer -= Time.deltaTime;
                    if (_alarmTimer <= 0f) { _audio.PlayOneShot(ProceduralAudio.Alarm(), 0.6f); _alarmTimer = 3f; }
                    break;
                }

                case GamePhase.Visit:
                    _text.color = new Color(1f, 0.2f, 0.1f);
                    _text.text = "●REC 在室中";
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
