using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>
    /// 調べる画面で回す実物のモデル（種類 → Prefab）。ビルダーが Assets/Prefabs/Models/HQ/Inspect/ の Prefab を登録する。
    /// ※シーンに保存されるのでクラス名とファイル名を一致させてある
    /// </summary>
    public class InspectLibrary : MonoBehaviour
    {
        public static InspectLibrary Instance { get; private set; }

        [Serializable]
        public class Entry
        {
            public InspectKind Kind;
            public GameObject Prefab;
        }

        public List<Entry> Models = new List<Entry>();

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public GameObject Get(InspectKind kind)
        {
            foreach (var e in Models)
                if (e != null && e.Kind == kind) return e.Prefab;
            return null;
        }
    }
}
