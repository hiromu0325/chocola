using UnityEngine;
using UnityEngine.Playables;

namespace EscapeProto
{
    /// <summary>
    /// 起床カットシーン（チュートリアル冒頭）。
    /// ゲーム開始（Playing）になった最初のフレームで、ベッドから起き上がるTimelineを再生する。
    /// つづきから再開した場合（進行がある場合）は再生しない。
    /// </summary>
    public class LoopIntro : MonoBehaviour
    {
        [Tooltip("起床カットシーンのPlayableDirector")]
        public PlayableDirector Director;
        [Tooltip("カットシーン用カメラ（普段は非アクティブ）")]
        public GameObject IntroCamera;

        private bool _armed;

        private void Update()
        {
            if (StoryProgress.IntroPlayed || _armed) return;
            if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
            if (LoopRooms.CurrentRoomId != LoopProgress.StartRoomId) return;
            if (CutsceneDirector.Instance == null || Director == null) return;
            if (CutsceneDirector.Instance.IsPlaying) return;

            _armed = true;
            StoryProgress.IntroPlayed = true;
            if (IntroCamera != null) IntroCamera.SetActive(true);
            CutsceneDirector.Instance.Finished += HandleFinished;
            CutsceneDirector.Instance.Play(Director);
        }

        private void HandleFinished()
        {
            if (CutsceneDirector.Instance != null)
                CutsceneDirector.Instance.Finished -= HandleFinished;
            if (IntroCamera != null) IntroCamera.SetActive(false);
        }
    }
}
