using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>黒塗りの資料1件（文面の［答え］が空欄になる）</summary>
    public class FillDocDef
    {
        public string RoomId, Id;
        public string Title;
        public string Template;          // ［答え］が黒塗り
        public string[] Hints;           // 空欄ごとの付箋を立てる手帳エントリ（答えが書いてある資料）
        public string Comment;           // 埋まった時の主人公のひと言（空行で区切ると順に出る。錠の音の一文は後に足す）
        public string ContainerName;     // 開く入れ物の呼び名（トースト用）

        public string EntryId => RoomId + "_" + Id;
        /// <summary>解けた印（LoopProgress の発見キー room/これ）。入れ物はこれを見て開く</summary>
        public string SolvedId => Id + "_solved";

        private string Key => $"fill/{RoomId}.{Id}";
        public string TitleText => GameText.Get(Key + ".title", Title);
        public string TemplateText => GameText.Get(Key + ".body", Template).Replace("\r", "");
        public string CommentText => GameText.Get(Key + ".comment", Comment);
    }

    /// <summary>黒塗りの資料の文面を、地の文と空欄に分けたもの</summary>
    public struct FillPart
    {
        public string Text;     // 地の文（空欄なら答え）
        public bool Blank;
        public int Index;       // 空欄の番号（0から）
    }

    /// <summary>手帳のメモのボードの上の、黒塗りの資料の置き場所（セーブに残る）</summary>
    [Serializable]
    public class FillBoardPos
    {
        public string id;        // 手帳エントリId（部屋_資料）
        public bool placed;
        public float x, y;
        public bool removed;     // 解いた後にボードから外した（資料の一覧には綴じてある）
    }

    /// <summary>
    /// 黒塗りの資料（穴埋め）。部屋で見つけると手帳のメモのボードに置かれ、マーカーで切り取った言葉のカードを
    /// 空欄へ運んで埋める。全部の空欄が埋まったら判定し、
    /// ・正しければ全文が読めるようになり（資料の一覧に綴じる）、その部屋の閉ざされた入れ物が開く（FillContainer）。
    ///   置いたカードは、まだ解いていない空欄の答えになる言葉ならボードへ戻り、もう使わない言葉は消える
    /// ・違えば、間違えた空欄の答えがある資料に付箋を立てる（罰は無い。カードは置いたまま）
    /// 空欄に置いたカードは MemoSnippet.slot に置き場所を持つ（ボードには出ない）。
    /// </summary>
    public static class FillDocs
    {
        public enum Result { None, Wrong, Solved }

        public static readonly FillDocDef[] All =
        {
            new FillDocDef
            {
                RoomId = "lab", Id = "guide", Title = "来客用の案内状", ContainerName = "ガラス戸の書類棚",
                Template =
                    "《ご来訪の皆さまへ》\n\n" +
                    "当研究所では、損傷した記憶領域をAIで補完し、\n" +
                    "［人格の連続性］を維持する研究を進めております。\n\n" +
                    "本日のご案内は、［主任研究員補佐］の［佐伯］が務めます。\n\n" +
                    "　　　　　　　　　小川脳神経総合研究所",
                Hints = new[] { "lab_summary", "lab_members", "lab_members" },
                Comment = "佐伯……名簿で、ひとりだけ顔の残っていた男だ。",
            },
            new FillDocDef
            {
                RoomId = "saeki_home", Id = "diarypage", Title = "佐伯の日記（欠けたページ）", ContainerName = "書斎机の引き出し",
                Template =
                    "《日記》\n\n" +
                    "4月2日　所長から「例の件」を打ち明けられた。\n" +
                    "病棟の、名前の読めない少女。\n" +
                    "その父親は［毎日面会に来られる］。\n" +
                    "所長は、その人を研究所に招いたのだという。\n\n" +
                    "4月9日　補完が、［研究員］の脳情報で学習している。\n" +
                    "所長の返事は「［今は結論を出すな］」。\n\n" +
                    "4月21日　今夜、［解析室］で、あの人と話す。\n" +
                    "もし僕に何かあったら──机の引き出しを。",
                Hints = new[] { "ward_girlfile", "analysis_anomaly", "study_scrap", "saeki_home_plog" },
                Comment = "「例の件」は、あの少女のことだったのか。",
            },
            new FillDocDef
            {
                RoomId = "ward", Id = "page303", Title = "303号の観察記録（欠けたページ）", ContainerName = "水野のロッカー",
                Template =
                    "《患者観察記録》　記録者: 水野\n\n" +
                    "・303号　名前は、まだ書かない。お父さんとの約束。\n" +
                    "　今日も［お父さんの声のときだけ］、目が動いた。\n" +
                    "　絵本は『［おやすみ］』。\n" +
                    "　最後のページで、いつも声が小さくなる。\n\n" +
                    "・私がこの仕事を選んだのは、［眠っている人を起こす］ため。\n" +
                    "　この子を起こすのは、薬じゃない。たぶん、あの声だ。",
                Hints = new[] { "ward_tape", "ward_tape", "mizuno_apart_diary" },
                Comment = "303号。……あの子の担当は、水野だったのか。",
            },
            new FillDocDef
            {
                RoomId = "kuroda_home", Id = "kmemo2", Title = "黒田のメモ（破り取られた後半）", ContainerName = "書き物机",
                Template =
                    "《個人メモ　後半》　筆跡: 黒田\n\n" +
                    "「──と、書いたのは間違いだった。\n" +
                    "主任はその夜、［札幌］にいた。\n" +
                    "第12研究室に入ったのは、［主任じゃない］。\n" +
                    "主任のIDを持ち出せたのは、主任に招かれ、\n" +
                    "書斎に出入りできた人間だけだ。\n" +
                    "［二宮秀樹］。毎晩、病棟で絵本を読んでいる、あの父親だ。\n" +
                    "明日、本人と話す。［正しいこと］をしに行く」",
                Hints = new[] { "data_room_minutes", "system_room_gaplog", "study_invite", "kuroda_home_rules" },
                Comment = "黒田は、私だと分かっていて、それでも会いに来た。",
            },
        };

        private static List<FillBoardPos> _board = new List<FillBoardPos>();

        /// <summary>空欄にカードを置いた／外した・判定した（ボードを描き直す）</summary>
        public static event Action OnChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _board = new List<FillBoardPos>();
            OnChanged = null;
        }

        // ============================== 定義 ==============================

        public static FillDocDef Get(string entryId)
        {
            foreach (var d in All) if (d.EntryId == entryId) return d;
            return null;
        }

        public static FillDocDef Get(string roomId, string id) => Get(roomId + "_" + id);

        /// <summary>文面を地の文と空欄に分ける</summary>
        public static List<FillPart> Parse(string template)
        {
            var parts = new List<FillPart>();
            int i = 0, blank = 0;
            while (i < template.Length)
            {
                int a = template.IndexOf('［', i);
                int b = a >= 0 ? template.IndexOf('］', a + 1) : -1;
                if (a < 0 || b < 0) { parts.Add(new FillPart { Text = template.Substring(i) }); break; }
                if (a > i) parts.Add(new FillPart { Text = template.Substring(i, a - i) });
                parts.Add(new FillPart { Text = template.Substring(a + 1, b - a - 1), Blank = true, Index = blank++ });
                i = b + 1;
            }
            return parts;
        }

        public static List<string> Answers(FillDocDef d)
        {
            var list = new List<string>();
            foreach (var p in Parse(d.TemplateText)) if (p.Blank) list.Add(p.Text);
            return list;
        }

        /// <summary>空欄を黒塗り（■）にした文面（部屋で拾った紙に載せる）</summary>
        public static string Redacted(FillDocDef d)
        {
            var sb = new StringBuilder();
            foreach (var p in Parse(d.TemplateText))
                sb.Append(p.Blank ? new string('■', Mathf.Clamp(Mathf.CeilToInt(Em(p.Text)), 2, 12)) : p.Text);
            return sb.ToString();
        }

        /// <summary>全部の空欄を答えで埋めた文面（解いた後に資料の一覧へ綴じる）</summary>
        public static string Full(FillDocDef d)
        {
            var sb = new StringBuilder();
            foreach (var p in Parse(d.TemplateText)) sb.Append(p.Text);
            return sb.ToString();
        }

        public static float Em(string s)
        {
            float w = 0f;
            foreach (char c in s) w += JaLineBreak.Em(c);
            return w;
        }

        // ============================== 状態 ==============================

        public static bool IsFound(FillDocDef d) => LoopProgress.IsFound(d.RoomId, d.Id);
        public static bool IsSolved(FillDocDef d) => LoopProgress.IsFound(d.RoomId, d.SolvedId);

        /// <summary>ボードに出ている黒塗りの資料（見つけた順ではなく定義の順）</summary>
        public static List<FillDocDef> OnBoard()
        {
            var list = new List<FillDocDef>();
            foreach (var d in All)
                if (IsFound(d) && !Pos(d).removed) list.Add(d);
            return list;
        }

        /// <summary>ボードの上の置き場所（無ければ作る）</summary>
        public static FillBoardPos Pos(FillDocDef d)
        {
            var p = _board.Find(b => b.id == d.EntryId);
            if (p == null) { p = new FillBoardPos { id = d.EntryId }; _board.Add(p); }
            return p;
        }

        public static string SlotKey(FillDocDef d, int index) => $"{d.EntryId}#{index}";

        /// <summary>その空欄に置いてあるカード（無ければ null）</summary>
        public static MemoSnippet InSlot(FillDocDef d, int index)
        {
            string key = SlotKey(d, index);
            foreach (var s in MemoSnippets.All) if (s.slot == key) return s;
            return null;
        }

        /// <summary>部屋で見つけた：ボードへ置く（置き場所はボードが決める）</summary>
        public static void NotifyFound(FillDocDef d)
        {
            var p = Pos(d);
            p.removed = false;
            OnChanged?.Invoke();
        }

        /// <summary>解いた資料をボードから外す（資料の一覧には綴じてある）。解く前は外せない</summary>
        public static bool RemoveFromBoard(FillDocDef d)
        {
            if (!IsSolved(d)) return false;
            Pos(d).removed = true;
            OnChanged?.Invoke();
            return true;
        }

        // ============================== 置く・外す・判定 ==============================

        /// <summary>
        /// カードを空欄に置く（前に置いてあったカードはボードへ戻す）。全部の空欄が埋まったら判定する。
        /// wrongBlanks には判定で違った空欄の番号が入る
        /// </summary>
        public static Result Place(MemoSnippet s, FillDocDef d, int index, List<int> wrongBlanks = null)
        {
            if (s == null || d == null || IsSolved(d)) return Result.None;
            var old = InSlot(d, index);
            if (old == s) return Result.None;
            if (old != null) { old.slot = null; old.placed = false; }   // 戻すカードはボードの空いた所へ置き直す
            s.slot = SlotKey(d, index);
            MemoSnippets.NotifyChanged();
            OnChanged?.Invoke();
            return JudgeIfFull(d, wrongBlanks);
        }

        /// <summary>空欄のカードをボードへ戻す</summary>
        public static void Unplace(MemoSnippet s)
        {
            if (s == null || string.IsNullOrEmpty(s.slot)) return;
            s.slot = null;
            s.placed = false;   // ボードの空いた所へ置き直す（元の場所はほかのカードが使っているかもしれない）
            MemoSnippets.NotifyChanged();
            OnChanged?.Invoke();
        }

        /// <summary>空欄のカードをボードの決まった場所（左上の角）へ戻す（空欄からドラッグで引き出して放した所）</summary>
        public static void Unplace(MemoSnippet s, Vector2 at)
        {
            if (s == null || string.IsNullOrEmpty(s.slot)) return;
            s.slot = null;
            s.placed = true;
            s.x = at.x;
            s.y = at.y;
            MemoSnippets.NotifyChanged();
            OnChanged?.Invoke();
        }

        private static Result JudgeIfFull(FillDocDef d, List<int> wrongBlanks)
        {
            var answers = Answers(d);
            var placed = new MemoSnippet[answers.Count];
            for (int i = 0; i < answers.Count; i++)
            {
                placed[i] = InSlot(d, i);
                if (placed[i] == null) return Result.None;   // まだ空欄がある
            }

            wrongBlanks?.Clear();
            var wrong = new List<int>();
            for (int i = 0; i < answers.Count; i++)
                if (!Fits(placed[i].text, answers[i])) wrong.Add(i);
            if (wrong.Count > 0)
            {
                // 違った空欄の答えがある資料に付箋（どの空欄が違ったかは言わない）
                foreach (int i in wrong)
                    if (d.Hints != null && i < d.Hints.Length) Notebook.Flag(d.Hints[i]);
                wrongBlanks?.AddRange(wrong);
                return Result.Wrong;
            }

            // 正解：全文を綴じ、解けた印を付ける（入れ物が開く）
            if (d.Hints != null) foreach (var h in d.Hints) Notebook.Unflag(h);
            Notebook.Add(d.EntryId, d.TitleText, Full(d));
            LoopProgress.NotifyFound(d.RoomId, d.SolvedId);

            // 置いたカード：まだ解いていない空欄の答えになる言葉はボードへ戻し、もう使わない言葉は消す
            foreach (var s in placed)
            {
                if (StillUseful(s.text)) { s.slot = null; s.placed = false; }
                else MemoSnippets.Remove(s.id);
            }
            MemoSnippets.NotifyChanged();
            OnChanged?.Invoke();
            return Result.Solved;
        }

        /// <summary>切り取った言葉が空欄にはまるか（答えを含んでいればよい。空白・全角半角の違いは見ない）</summary>
        public static bool Fits(string text, string answer)
        {
            string a = Normalize(answer);
            return a.Length > 0 && Normalize(text).Contains(a);
        }

        /// <summary>まだ解いていない黒塗りの資料（まだ見つけていないものも）のどれかの空欄の答えを含むか</summary>
        public static bool StillUseful(string text)
        {
            foreach (var d in All)
            {
                if (IsSolved(d)) continue;
                foreach (var a in Answers(d))
                    if (Fits(text, a)) return true;
            }
            return false;
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Normalize(NormalizationForm.FormKC);
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) if (!char.IsWhiteSpace(c)) sb.Append(c);
            return sb.ToString();
        }

        // ============================== セーブ連携 ==============================

        public static void Clear()
        {
            _board = new List<FillBoardPos>();
            OnChanged?.Invoke();
        }

        public static List<FillBoardPos> Export() => new List<FillBoardPos>(_board);

        public static void Import(List<FillBoardPos> list)
        {
            _board = list != null ? new List<FillBoardPos>(list) : new List<FillBoardPos>();
            OnChanged?.Invoke();
        }

        /// <summary>手帳のボードの置き場所を全部忘れる（並べ直し）</summary>
        public static void ResetPlacement()
        {
            foreach (var p in _board) p.placed = false;
        }
    }
}
