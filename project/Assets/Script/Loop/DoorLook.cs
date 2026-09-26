using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 回廊の扉の見た目。部屋へ入れるようになると、回廊の共通の扉からその部屋の入口の扉
    /// （部屋の外側の面を回廊へ向けたもの）に変わる。以後は施錠に戻らない。
    /// 変わるのは回廊が表示された時（部屋にいる間に解錠されるので、次に回廊へ出ると変わっている）。
    /// ※クラス名とファイル名の一致が必須（シーン保存時のスクリプト解決）
    /// </summary>
    public class DoorLook : MonoBehaviour
    {
        public string RoomId;
        public DoorSwing Swing;
        [Tooltip("施錠中の回廊の扉（蝶番の軸に置いた親）")]
        public GameObject CorridorLeaf;
        public float CorridorAngle = 80f;
        [Tooltip("入れるようになった後の、部屋の入口の扉（蝶番の軸に置いた親）")]
        public GameObject RoomLeaf;
        public float RoomAngle = -80f;

        private bool _roomLook;
        private bool _applied;
        private float _next;

        private void OnEnable()
        {
            _next = 0f;
            Refresh();   // 回廊が表示されたその場で見た目を合わせる（着いた扉を開いた姿勢にする前に）
        }

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.25f;
            Refresh();
        }

        private void Refresh()
        {
            bool room = RoomLeaf != null && !string.IsNullOrEmpty(RoomId) && LoopRooms.IsUnlocked(RoomId);
            if (_applied && room == _roomLook) return;
            _applied = true;
            _roomLook = room;
            if (CorridorLeaf != null) CorridorLeaf.SetActive(!room);
            if (RoomLeaf != null) RoomLeaf.SetActive(room);
            if (Swing != null)
            {
                var leaf = room ? RoomLeaf : CorridorLeaf;
                if (leaf != null) Swing.UseLeaf(leaf.transform, room ? RoomAngle : CorridorAngle);
            }
        }
    }
}
