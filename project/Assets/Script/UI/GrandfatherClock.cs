using UnityEngine;

namespace EscapeProto
{
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
