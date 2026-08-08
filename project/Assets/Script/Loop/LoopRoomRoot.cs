using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 仮想部屋のルート。回廊の扉割り当て(Side/Slot)、解錠段階、出入口スポーン地点を持つ。
    /// アクティブなのは常に最大1部屋（RoomTransitionSystemが切替える）。
    /// ※クラス名とファイル名の一致が必須（シーン保存時のスクリプト解決）
    /// </summary>
    public class LoopRoomRoot : MonoBehaviour
    {
        public string Id;
        public string DisplayName;
        [Tooltip("この進行度以上で入れる（0=最初から）")]
        public int UnlockStage;
        [Tooltip("回廊の扉の辺(0=N,1=E,2=S,3=W)とスロット")]
        public int Side;
        public int Slot;
        [Tooltip("入口側(南)のスポーン地点")]
        public Transform EntrySpawn;
        [Tooltip("出口側(北)のスポーン地点")]
        public Transform ExitSpawn;
        [Tooltip("この部屋のブレイカー")]
        public BreakerSwitch Breaker;
        [Tooltip("次の部屋を解放するために見つける必要があるアイテムのId")]
        public string[] RequiredFindables = new string[0];

        private void Awake() => LoopRooms.Register(this);
        private void OnDestroy() => LoopRooms.Unregister(this);
    }
}
