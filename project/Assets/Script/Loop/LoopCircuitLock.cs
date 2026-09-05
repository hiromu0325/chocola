using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 適用型ギミック：記憶回路。タイルを回して光を通し、"欠損"セルの傾向記号を選ぶ。
    /// 規則は盤面に書かない。根拠資料（補完アルゴリズム仕様）に
    /// 「欠損は両隣の傾向から推定して埋める」とだけ書いてある。
    /// </summary>
    public class LoopCircuitLock : LoopLockBase
    {
        public string Title = "記憶回路";
        [TextArea] public string Body = "提供体の記憶を対象者へ通す。欠損した区画には傾向を補完すること。";
        public LoopPuzzleUI.CircuitLevel Level;
        [Tooltip("光が通ったときに点灯させるレンダラー（任意）")]
        public Renderer Lamp;

        protected override void Begin()
        {
            if (LoopPuzzleUI.Instance == null) return;
            LoopPuzzleUI.Instance.ShowCircuit(Title, Body, Level,
                solved => { if (solved) Succeed("光が通った。記憶回路が繋がった"); },
                cell => Wrong("補完が噛み合わない。"));
        }

        protected override void OnSolved()
        {
            if (Lamp != null) Lamp.material.color = new Color(0.4f, 0.8f, 1f);
        }

        private void Start() { if (Solved) OnSolved(); }

        // ---- 盤面のプリセット（ビルダーから呼ぶ）----

        /// <summary>
        /// 1章転（解析室）5×5。入力=左の2行目、出力=右の0行目。
        /// 経路: (2,0)━ (2,1)?[欠損] (2,2)━ (2,3)┗上 (1,3)? [欠損] (1,2)... ではなく
        /// 下記の固定レイアウト。欠損は「両隣が同じ傾向」になる場所に置く。
        /// </summary>
        public static LoopPuzzleUI.CircuitLevel Level5x5()
        {
            const int n = 5;
            var lv = new LoopPuzzleUI.CircuitLevel { Size = n, InRow = 2, OutRow = 4 };
            var P = LoopPuzzleUI.Pipe.None;
            var S = LoopPuzzleUI.Pipe.Straight;
            var C = LoopPuzzleUI.Pipe.Corner;
            var T = LoopPuzzleUI.Pipe.Tee;
            // 経路: (2,0)S → (2,1)S[欠損:○] → (2,2)S → (2,3)C(左・下) → (3,3)S縦[欠損:△] → (4,3)C(上・右) → (4,4)S → 出力
            // その他は迷わせ用の残骸（経路に繋がらない）
            lv.Pipes = new[]
            {
                C, S, P, T, S,
                P, C, S, P, C,
                S, S, S, C, P,
                C, P, S, S, C,
                P, T, P, C, S,
            };
            //           行0            行1            行2            行3            行4
            lv.Symbols = new[]
            {
                2, 1, -1, 2, 1,
                -1, 2, 1, -1, 2,
                0, -1, 0, 1, -1,
                2, -1, 0, -1, 2,
                -1, 1, -1, 1, 1,
            };
            lv.Answers = new int[n * n];
            for (int i = 0; i < lv.Answers.Length; i++) lv.Answers[i] = -1;
            lv.Answers[2 * n + 1] = 0;   // (2,1): 両隣 (2,0)○ と (2,2)○ → ○
            lv.Answers[3 * n + 3] = 1;   // (3,3): 両隣 (2,3)△ と (4,3)△ → △
            // 経路セルの傾向を確定（欠損以外）
            lv.Symbols[2 * n + 0] = 0; lv.Symbols[2 * n + 2] = 0;
            lv.Symbols[2 * n + 3] = 1; lv.Symbols[4 * n + 3] = 1; lv.Symbols[4 * n + 4] = 1;
            // 欠損(3,3)の左右の残骸も△にして「両隣」の解釈で迷わせない
            lv.Symbols[3 * n + 2] = 1; lv.Symbols[3 * n + 4] = 1;
            // 欠損セル以外の -1 はダミーの残骸なので適当な記号にしておく（迷わせ用は"?"にしない）
            for (int i = 0; i < lv.Symbols.Length; i++)
                if (lv.Symbols[i] < 0 && lv.Answers[i] < 0) lv.Symbols[i] = (i * 7) % 3;
            // 初期回転はバラバラ（正解は: (2,0)0 (2,1)0 (2,2)0 (2,3)C→左・下=回転2 (3,3)S縦=1 (4,3)C→上・右=0 (4,4)0）
            lv.Rotations = new int[n * n];
            for (int i = 0; i < lv.Rotations.Length; i++) lv.Rotations[i] = (i * 5 + 3) % 4;
            return lv;
        }
    }
}
