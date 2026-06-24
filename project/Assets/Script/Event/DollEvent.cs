using System.Collections;
using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// 笑い声イベント＝日本人形イベント。
    /// 別部屋に金髪・和装・閉眼の人形が出現。部屋に入ると話しかけてくる。
    ///   ・選択肢を選んで答える → 死亡確定
    ///   ・20秒間無視する     → 生存（人形は諦める）
    ///   ・『目』を2つ渡す     → 目が開き髪が黒く変化 → ロング黒髪の探索者へ（死）
    /// 正解は「無視」。手帳とモニターの情報だけで判断できる（ひらめき不要）。
    /// </summary>
    public class DollEvent : MonoBehaviour
    {
        [Header("人形の見た目")]
        [SerializeField] private Renderer[] _hairRenderers;   // 髪（黒化対象）
        [SerializeField] private Renderer[] _eyeRenderers;    // 目（開眼で発光）

        [Header("挙動")]
        [Tooltip("話しかけられてから無視で生存できる秒数")]
        [SerializeField] private float _ignoreSeconds = 20f;
        [Tooltip("部屋に入らない場合でもイベントが終わる安全時間")]
        [SerializeField] private float _maxEventSeconds = 120f;

        private bool _active;
        private bool _hasSpoken;
        private bool _resolved;
        private float _ignoreTimer;
        private float _eventTimer;
        private int _eyesHeld;

        private void OnEnable()
        {
            GameEvents.OnLaughterEventStart += Begin;
            GameEvents.OnGameOver += ForceEnd;
            GameEvents.OnGameClear += ForceEnd;
        }
        private void OnDisable()
        {
            GameEvents.OnLaughterEventStart -= Begin;
            GameEvents.OnGameOver -= ForceEnd;
            GameEvents.OnGameClear -= ForceEnd;
        }

        private void Start()
        {
            SetBlondeClosed();
            gameObject.SetActive(true); // 子のトリガー/見た目は常駐、ロジックで制御
            _active = false;
        }

        private void Begin()
        {
            _active = true; _hasSpoken = false; _resolved = false;
            _eyesHeld = 0; _eventTimer = _maxEventSeconds;
            SetBlondeClosed();
        }

        private void ForceEnd()
        {
            _active = false; _resolved = true;
            if (HUDManager.Instance != null) HUDManager.Instance.HideDialogue();
        }

        private void Update()
        {
            if (!_active || _resolved) return;

            _eventTimer -= Time.deltaTime;
            if (_eventTimer <= 0f) { Survive("…つまらない。どこにもいないのね。"); return; }

            if (_hasSpoken)
            {
                _ignoreTimer -= Time.deltaTime;
                if (_ignoreTimer <= 0f) Survive("…ふぅん。きこえないふり。\nもう、いいわ。");
            }
        }

        /// <summary>無視してやり過ごし生存（正解ルート）。イベントを閉じ、探索フェーズへ戻す</summary>
        private void Survive(string line)
        {
            if (_resolved) return;
            _resolved = true; _active = false;
            if (HUDManager.Instance != null)
            {
                HUDManager.Instance.HideDialogue();
                HUDManager.Instance.ShowSubtitle(line, 3f);
            }
            GameEvents.RaiseLaughterEventEnd();   // 笑い声イベント終了 → 時計・探索が再開
        }

        /// <summary>子のトリガーゾーンから呼ばれる：プレイヤーが部屋に入った</summary>
        public void OnPlayerEntered()
        {
            if (!_active || _resolved || _hasSpoken) return;
            _hasSpoken = true;
            _ignoreTimer = _ignoreSeconds;
            if (HUDManager.Instance != null)
            {
                HUDManager.Instance.ShowDialogue(
                    "金髪の人形",
                    "ねえ……そこにいるんでしょう？\nこっちを向いて。お話ししましょう？",
                    new[] { "返事をする", "「誰？」と尋ねる" },
                    OnAnswered);
            }
        }

        private void OnAnswered(int idx)
        {
            if (_resolved) return;
            _resolved = true; _active = false;
            if (HUDManager.Instance != null)
                HUDManager.Instance.ShowSubtitle("「……みつけた。」", 3f);
            GameEvents.RaiseSpecialDeath("doll_answer");
        }

        /// <summary>EyeItem から呼ばれる：目を1つ拾った</summary>
        public void AddEye()
        {
            _eyesHeld = Mathf.Min(2, _eyesHeld + 1);
            if (HUDManager.Instance != null)
                HUDManager.Instance.ShowSubtitle($"硝子の目を拾った（{_eyesHeld}/2）", 2.5f);
        }

        /// <summary>人形本体へのインタラクト：目を渡そうとする（罠）</summary>
        public void TryGiveEyes()
        {
            if (!_active || _resolved) return;
            if (_eyesHeld >= 2)
            {
                _resolved = true; _active = false;
                StartCoroutine(TransformAndKill());
            }
            else if (HUDManager.Instance != null)
            {
                HUDManager.Instance.ShowDialogue(
                    "金髪の人形",
                    "わたし、目が無いの。\nお願い、探して持ってきて……",
                    new[] { "（その場を離れる）" },
                    _ => { });
            }
        }

        private IEnumerator TransformAndKill()
        {
            if (HUDManager.Instance != null)
                HUDManager.Instance.ShowSubtitle("「ありがとう。」", 2f);
            SetEyesOpen();
            yield return new WaitForSeconds(1.5f);

            // 髪が黒く変わる → ロング黒髪の探索者と似た風貌へ
            SetHairBlack();
            if (HUDManager.Instance != null)
                HUDManager.Instance.ShowSubtitle("「そこにいたんだね。みーつけた。」", 2.5f);
            yield return new WaitForSeconds(1.5f);

            GameEvents.RaiseSpecialDeath("doll_eyes");
        }

        // ============= 見た目操作 =============
        private void SetBlondeClosed()
        {
            SetHair(new Color(0.92f, 0.82f, 0.35f));   // 金髪
            SetEyeEmission(0f);
        }
        private void SetEyesOpen() => SetEyeEmission(2.5f);
        private void SetHairBlack() => SetHair(new Color(0.02f, 0.02f, 0.03f));

        private void SetHair(Color c)
        {
            if (_hairRenderers == null) return;
            foreach (var r in _hairRenderers) if (r != null) r.material.color = c;
        }
        private void SetEyeEmission(float intensity)
        {
            if (_eyeRenderers == null) return;
            foreach (var r in _eyeRenderers)
            {
                if (r == null) continue;
                var c = new Color(0.8f, 0.1f, 0.1f);
                if (intensity > 0f)
                {
                    r.material.color = c;
                    r.material.EnableKeyword("_EMISSION");
                    r.material.SetColor("_EmissionColor", c * intensity);
                }
                else
                {
                    r.material.color = new Color(0.1f, 0.08f, 0.08f);
                    r.material.SetColor("_EmissionColor", Color.black);
                }
            }
        }
    }

    /// <summary>人形の部屋への侵入を検知するトリガーゾーン</summary>
    public class DollTriggerZone : MonoBehaviour
    {
        [SerializeField] private DollEvent _event;
        public void SetEvent(DollEvent e) => _event = e;

        private void OnTriggerEnter(Collider other)
        {
            if (_event == null) return;
            if (other.CompareTag("Player") || other.GetComponent<PlayerStatus>() != null)
                _event.OnPlayerEntered();
        }
    }

    /// <summary>人形本体：インタラクトで目を渡そうとする（罠）</summary>
    public class DollInteract : MonoBehaviour, IInteractable, IPromptProvider
    {
        [SerializeField] private DollEvent _event;
        public void SetEvent(DollEvent e) => _event = e;

        private float _lastCallTime = -10f;
        public bool CanInteract => GameManager.Instance == null || !GameManager.Instance.IsGameEnded;

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;
            if (_event != null) _event.TryGiveEyes();
        }

        public string GetPrompt() => "[E] 人形に近づく…";
        public float GetProgress01() => -1f;
    }

    /// <summary>落ちている『硝子の目』：拾うと人形に渡せるようになる（だが渡すと死ぬ）</summary>
    public class EyeItem : MonoBehaviour, IInteractable, IPromptProvider
    {
        private float _lastCallTime = -10f;
        public bool CanInteract => true;

        public void OnInteract()
        {
            bool isNew = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNew) return;

            var ev = FindFirstObjectByType<DollEvent>();
            if (ev != null) ev.AddEye();
            Destroy(gameObject);
        }

        public string GetPrompt() => "[E] 硝子の目を拾う";
        public float GetProgress01() => -1f;
    }
}
