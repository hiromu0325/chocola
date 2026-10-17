using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeProto
{
    /// <summary>
    /// マーカーで拾った文字を白くして、影を付けて浮き上がらせる（Text の頂点の色を書き換え、影の四角形を下に足す）。
    /// Text は半角の空白・改行の四角形を作らないので、それ以外の文字を順に数えて四角形と対応させる。
    /// ※AddComponent で作るのでファイル名と一致させてある
    /// </summary>
    [RequireComponent(typeof(Text))]
    public class MarkerGlyphs : BaseMeshEffect
    {
        public Color Lit = Color.white;
        /// <summary>影（近い濃い影＋少し離れた薄い影で、紙から持ち上がって見せる）</summary>
        public Color Shadow = new Color(0f, 0f, 0f, 0.8f);
        public Vector2 ShadowNear = new Vector2(1f, -1.5f), ShadowFar = new Vector2(2f, -3f);

        private readonly HashSet<int> _lit = new HashSet<int>();
        private readonly List<UIVertex> _stream = new List<UIVertex>();
        private readonly List<UIVertex> _out = new List<UIVertex>();
        private readonly List<int> _map = new List<int>();

        /// <summary>浮き上がらせる文字（本文の文字番号）。変わった時だけ作り直す</summary>
        public void Set(HashSet<int> chars)
        {
            if (_lit.SetEquals(chars)) return;
            _lit.Clear();
            _lit.UnionWith(chars);
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || _lit.Count == 0) return;
            var text = graphic as Text;
            if (text == null) return;
            string s = text.text ?? "";
            vh.GetUIVertexStream(_stream);   // 1文字 = 三角形2つ = 6頂点
            int quads = _stream.Count / 6;

            // 四角形の順番 → 本文の文字番号
            _map.Clear();
            for (int i = 0; i < s.Length && _map.Count < quads; i++)
                if (HasQuad(s[i])) _map.Add(i);
            if (_map.Count < quads) return;   // 対応が取れない時は何もしない

            _out.Clear();
            AddShadows(quads, ShadowFar, Shadow.a * 0.35f);
            AddShadows(quads, ShadowNear, Shadow.a);
            for (int q = 0; q < quads; q++)
            {
                bool lit = _lit.Contains(_map[q]);
                for (int k = 0; k < 6; k++)
                {
                    var v = _stream[q * 6 + k];
                    if (lit)
                    {
                        Color32 c = Lit;
                        c.a = v.color.a;
                        v.color = c;
                    }
                    _out.Add(v);
                }
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(_out);
        }

        /// <summary>Text が四角形を作る文字か（半角の空白・タブ・改行は作らない。全角の空白は作る）</summary>
        private static bool HasQuad(char c) => c != ' ' && c != '\t' && c != '\n' && c != '\r';

        /// <summary>拾った文字の影（文字より先に描く＝文字の下になる）</summary>
        private void AddShadows(int quads, Vector2 offset, float alpha)
        {
            for (int q = 0; q < quads; q++)
            {
                if (!_lit.Contains(_map[q])) continue;
                for (int k = 0; k < 6; k++)
                {
                    var v = _stream[q * 6 + k];
                    v.position += (Vector3)offset;
                    Color32 c = Shadow;
                    c.a = (byte)(alpha * v.color.a);
                    v.color = c;
                    _out.Add(v);
                }
            }
        }
    }
}
