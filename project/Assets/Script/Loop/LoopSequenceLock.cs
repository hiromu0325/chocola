using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 転記型（順序）ギミック：資料に書かれた順番どおりに項目を選ぶ。
    /// 例）運用手順書 3-3「復電は 記憶野→言語野→自己認識野 の順」
    /// 1つ選ぶごとに次の選択を出す。途中で間違えると最初からやり直し（ペナルティ無し）。
    /// </summary>
    public class LoopSequenceLock : LoopLockBase
    {
        public string Title = "配電盤";
        [TextArea] public string Body = "どの系統から給電する？";
        [Tooltip("選択肢のラベル（表示順はこのまま。正解順は CorrectOrder）")]
        public string[] Steps;
        [Tooltip("Steps のインデックスを正しい順に並べたもの")]
        public int[] CorrectOrder;
        [Tooltip("1手ごとに点灯させるレンダラー（任意。Stepsと同数）")]
        public Renderer[] StepLamps;

        private int _progress;

        protected override void Begin()
        {
            _progress = 0;
            AskNext();
        }

        private void AskNext()
        {
            // 既に選んだものは出さない
            var remaining = new List<int>();
            for (int i = 0; i < Steps.Length; i++)
                if (!Chosen(i)) remaining.Add(i);

            var labels = remaining.ConvertAll(i => Steps[i]).ToArray();
            string body = Body + $"\n\n手順 {_progress + 1} / {Steps.Length}";
            PuzzleUI.Instance.ShowSelection(Title, body, labels, idx =>
            {
                if (idx < 0) { ResetLamps(); return; }   // 中止
                int step = remaining[idx];
                if (step == CorrectOrder[_progress])
                {
                    _progress++;
                    Lamp(step, true);
                    ProceduralAudio.PlayAt(ProceduralAudio.Click(), transform.position, 0.7f);
                    if (_progress >= CorrectOrder.Length) Succeed("順序どおりに給電した");
                    else AskNext();
                }
                else
                {
                    _progress = 0;
                    ResetLamps();
                    Wrong("系統が不安定になり、遮断された。順序が違う。");
                }
            });
        }

        private bool Chosen(int step)
        {
            for (int k = 0; k < _progress; k++) if (CorrectOrder[k] == step) return true;
            return false;
        }

        private void Lamp(int step, bool on)
        {
            if (StepLamps == null || step >= StepLamps.Length || StepLamps[step] == null) return;
            StepLamps[step].material.color = on ? new Color(0.3f, 1f, 0.45f) : new Color(0.5f, 0.1f, 0.1f);
        }

        private void ResetLamps()
        {
            if (StepLamps == null) return;
            for (int i = 0; i < StepLamps.Length; i++) Lamp(i, Solved);
        }

        protected override void OnSolved()
        {
            if (StepLamps == null) return;
            for (int i = 0; i < StepLamps.Length; i++) Lamp(i, true);
        }

        private void Start()
        {
            if (Solved) OnSolved(); else ResetLamps();
        }
    }
}
