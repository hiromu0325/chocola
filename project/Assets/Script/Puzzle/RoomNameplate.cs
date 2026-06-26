using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 社員個室のドア上の表札。実行時に TextMesh を生成（動的フォントの保存問題を回避）。
    /// </summary>
    public class RoomNameplate : MonoBehaviour
    {
        public string Label = "個室";

        private void Awake()
        {
            var tm = gameObject.GetComponent<TextMesh>();
            if (tm == null) tm = gameObject.AddComponent<TextMesh>();
            tm.font = FontProvider.Get();
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.material = tm.font.material;
            tm.text = Label;
            tm.fontSize = 64;
            tm.characterSize = 0.14f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.95f, 0.9f, 0.7f);
        }
    }
}
