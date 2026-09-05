using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// システム発のポップアップUI（解放ダイアログ・部屋名タイトル等）の待機列。
    /// 資料ウィンドウを読んでいる間・カットシーン中・別のポップアップ表示中は
    /// 次のUIを出さず、閉じられてから1つずつ順番に表示する。
    ///
    /// 使い方: UiQueue.Instance.Enqueue(表示アクション, 表示中判定, タグ)
    /// ※プレイヤーが自分で開くUI（資料を調べる等）は即時のままでよい。
    ///   これは「勝手に開くUI」が重ならないようにするための仕組み。
    /// </summary>
    public class UiQueue : MonoBehaviour
    {
        public static UiQueue Instance { get; private set; }

        private class Item
        {
            public Action Show;
            public Func<bool> IsShowing;
            public string Tag;
        }

        private readonly Queue<Item> _items = new Queue<Item>();
        private bool _pumping;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>
        /// ポップアップを待機列に積む。
        /// show: 表示を開始する処理／isShowing: まだ表示中ならtrueを返す判定
        /// </summary>
        public void Enqueue(Action show, Func<bool> isShowing, string tag = null)
        {
            _items.Enqueue(new Item { Show = show, IsShowing = isShowing, Tag = tag });
            if (!_pumping) StartCoroutine(Pump());
        }

        /// <summary>
        /// 他のUIが画面を使っている間はtrue（新しいポップアップを出してはいけない）。
        /// ※BlockReopen（閉じたEの離し待ち）は含めない：あれはプレイヤーの再オープン誤爆
        ///   防止であって、システム発のUIまで待たせるとテンポが悪くなる
        /// </summary>
        public static bool GlobalBusy =>
            (PuzzleUI.Instance != null && PuzzleUI.Instance.IsOpen) ||
            (CutsceneDirector.Instance != null && CutsceneDirector.Instance.IsPlaying) ||
            (RoomTitleUI.Instance != null && RoomTitleUI.Instance.IsShowing);

        private IEnumerator Pump()
        {
            _pumping = true;
            while (_items.Count > 0)
            {
                // 前のUIが閉じるまで待つ。待ち相手が帯（非モーダル）で、後ろがつかえて
                // いるなら帯を早送りしてもらう（閉じた直後のラグを作らない）
                while (GlobalBusy)
                {
                    if (RoomTitleUI.Instance != null && RoomTitleUI.Instance.IsShowing)
                        RoomTitleUI.Instance.Hurry();
                    yield return null;
                }

                var item = _items.Dequeue();
                item.Show?.Invoke();

                // 表示が立ち上がるのを待ってから「閉じられるまで」待機
                yield return null;
                yield return null;
                if (item.IsShowing != null)
                    while (item.IsShowing())
                    {
                        // この項目が帯で、次の項目が待っているなら早送り
                        if (_items.Count > 0 && item.Tag != null && item.Tag.StartsWith("title:") &&
                            RoomTitleUI.Instance != null)
                            RoomTitleUI.Instance.Hurry();
                        yield return null;
                    }

                // 連続表示の圧迫感を避ける小さな間
                yield return new WaitForSeconds(0.12f);
            }
            _pumping = false;
        }
    }
}
