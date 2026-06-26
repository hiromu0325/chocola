using System.Collections.Generic;
using UnityEngine;
using StarterAssets;

namespace EscapeProto
{
    /// <summary>
    /// 中央の机に並ぶ陶器の人形＝残機（5体）
    /// 死ぬたびに1体が砕ける。「そして誰もいなくなった」
    /// </summary>
    public class DollRack : MonoBehaviour
    {
        [SerializeField] private int _dollCount = 5;
        [SerializeField] private float _spacing = 0.22f;

        private readonly List<GameObject> _dolls = new List<GameObject>();

        private void Start()
        {
            if (GameManager.Instance != null) _dollCount = GameManager.Instance.Dolls;
            for (int i = 0; i < _dollCount; i++)
            {
                var doll = BuildPorcelainDoll(i);
                doll.transform.SetParent(transform, false);
                doll.transform.localPosition = new Vector3((i - (_dollCount - 1) * 0.5f) * _spacing, 0f, 0f);
                _dolls.Add(doll);
            }
            GameEvents.OnDollsChanged += HandleDollsChanged;
        }

        private void OnDestroy() => GameEvents.OnDollsChanged -= HandleDollsChanged;

        private void HandleDollsChanged(int remaining)
        {
            while (_dolls.Count > remaining && _dolls.Count > 0)
            {
                var doll = _dolls[_dolls.Count - 1];
                _dolls.RemoveAt(_dolls.Count - 1);
                Shatter(doll);
            }
        }

        private void Shatter(GameObject doll)
        {
            for (int i = 0; i < 8; i++)
            {
                var frag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                frag.transform.position = doll.transform.position + Random.insideUnitSphere * 0.08f;
                frag.transform.localScale = Vector3.one * 0.04f;
                var rb = frag.AddComponent<Rigidbody>();
                rb.AddForce(Random.onUnitSphere * 2f + Vector3.up * 1.5f, ForceMode.Impulse);
                var rend = frag.GetComponent<Renderer>();
                var src = doll.GetComponentInChildren<Renderer>();
                if (src != null) rend.material = src.material;
                Destroy(frag, 2.5f);
            }
            ProceduralAudio.PlayAt(ProceduralAudio.Click(), doll.transform.position, 1f);
            Destroy(doll);
        }

        private static GameObject BuildPorcelainDoll(int index)
        {
            var root = new GameObject($"PorcelainDoll_{index}");

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            // 陶器：白くつややか
            var mat = new Material(shader) { color = new Color(0.93f, 0.92f, 0.9f) };
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.85f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.85f);
            // 着物の色を個体差で
            var kimono = new Material(shader)
            { color = Color.HSVToRGB((index * 0.13f) % 1f, 0.4f, 0.7f) };

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            body.transform.localScale = new Vector3(0.07f, 0.08f, 0.07f);
            Object.Destroy(body.GetComponent<Collider>());
            body.GetComponent<Renderer>().material = kimono;

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            head.transform.localScale = Vector3.one * 0.08f;
            Object.Destroy(head.GetComponent<Collider>());
            head.GetComponent<Renderer>().material = mat;

            return root;
        }
    }
}
