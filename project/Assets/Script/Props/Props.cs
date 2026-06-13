using System.Collections.Generic;
using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// テーブル上の人形＝残機表示
    /// 死ぬたびに1体が「破壊」される（弾け飛ぶ演出付き）
    /// </summary>
    public class DollRack : MonoBehaviour
    {
        [SerializeField] private int _dollCount = 3;
        [SerializeField] private float _spacing = 0.35f;

        private readonly List<GameObject> _dolls = new List<GameObject>();

        private void Start()
        {
            // GameManager の残機数と同期
            if (GameManager.Instance != null) _dollCount = GameManager.Instance.Lives;

            for (int i = 0; i < _dollCount; i++)
            {
                var doll = BuildDoll(i);
                doll.transform.SetParent(transform, false);
                doll.transform.localPosition = new Vector3((i - (_dollCount - 1) * 0.5f) * _spacing, 0f, 0f);
                _dolls.Add(doll);
            }

            GameEvents.OnLivesChanged += HandleLivesChanged;
        }

        private void OnDestroy()
        {
            GameEvents.OnLivesChanged -= HandleLivesChanged;
        }

        private void HandleLivesChanged(int lives)
        {
            // 後ろから破壊
            while (_dolls.Count > lives && _dolls.Count > 0)
            {
                var doll = _dolls[_dolls.Count - 1];
                _dolls.RemoveAt(_dolls.Count - 1);
                ShatterDoll(doll);
            }
        }

        private void ShatterDoll(GameObject doll)
        {
            // 破片を飛ばして消す簡易演出
            for (int i = 0; i < 6; i++)
            {
                var frag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                frag.transform.position = doll.transform.position + Random.insideUnitSphere * 0.1f;
                frag.transform.localScale = Vector3.one * 0.05f;
                var rb = frag.AddComponent<Rigidbody>();
                rb.AddForce(Random.onUnitSphere * 2.5f + Vector3.up * 2f, ForceMode.Impulse);
                var rend = frag.GetComponent<Renderer>();
                rend.material = doll.GetComponentInChildren<Renderer>().material;
                Destroy(frag, 2.5f);
            }
            ProceduralAudio.PlayAt(ProceduralAudio.Click(), doll.transform.position, 1f);
            Destroy(doll);
        }

        private static GameObject BuildDoll(int index)
        {
            var root = new GameObject($"Doll_{index}");

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.09f, 0f);
            body.transform.localScale = new Vector3(0.08f, 0.09f, 0.08f);
            Object.Destroy(body.GetComponent<Collider>());

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            head.transform.localScale = Vector3.one * 0.09f;
            Object.Destroy(head.GetComponent<Collider>());

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { color = new Color(0.95f, 0.85f, 0.7f) };
            body.GetComponent<Renderer>().material = mat;
            head.GetComponent<Renderer>().material = mat;

            return root;
        }
    }

    /// <summary>
    /// 敵の特徴がわかるメモ帳：インタラクトで閲覧（以降 Tab でも開閉可能）
    /// </summary>
    public class MemoNote : MonoBehaviour, IInteractable, IPromptProvider
    {
        // 押下中の毎フレーム呼び出しを無視し、新規押下のみトグル
        private float _lastCallTime = -10f;
        public bool CanInteract => true;

        public void OnInteract()
        {
            bool isNewPress = Time.time - _lastCallTime > 0.25f;
            _lastCallTime = Time.time;
            if (!isNewPress) return;

            if (HUDManager.Instance != null)
                HUDManager.Instance.ToggleMemo();
        }

        public string GetPrompt() => "[E] メモ帳を読む（Tabでも開閉）";
        public float GetProgress01() => -1f;
    }
}
