using System.Collections.Generic;
using UnityEngine;

namespace EscapeProto
{
    /// <summary>調べる画面で回す実物の種類（Assets/Models/HQ/Inspect/Inspect_&lt;種類&gt;.fbx）</summary>
    public enum InspectKind
    {
        None,        // 実物なし（文章だけ）
        Sheet, Report, Folder, Letter, Notebook, Card, Photo, Newspaper,
        Monitor, Cassette, Recorder, Poster, Clipboard, Drawing,
        TapeRecorder,   // カセットレコーダー（最初の部屋で拾う装備。テープはこれで聞く。リールが回る）
        Helmet,      // 部屋にある専用モデル（解析室のヘルメット）
    }

    /// <summary>紙面に載せる文字の書き方</summary>
    public enum InkStyle { Print, Hand, Screen, Crayon }

    /// <summary>資料1件の見せ方</summary>
    public struct DocInfo
    {
        public InspectKind Kind;
        public InkStyle Ink;
        public float Scale;     // 実物の大きさの倍率（付箋・切れ端は小さく）
        public Color Tint;      // 紙の色味（付箋の黄色など）。白＝そのまま
        public bool Audio;      // 音声記録（再生して聞く。聞き終えると書き起こしを読める）

        public bool HasModel => Kind != InspectKind.None;
    }

    /// <summary>音声記録の1行（話者が空なら物音・状況の説明）</summary>
    public struct TapeLine
    {
        public string Speaker;   // 表示名（松原・川崎…）。空＝物音
        public string Text;
    }

    /// <summary>
    /// 資料の見せ方の一覧と、主人公のひと言（字幕）・音声記録の台詞の引き口。
    /// 文面はテキスト表（GameText.xlsx → GameText.asset）から引き、表に無ければここの組み込み文を使う。
    ///   doc/&lt;部屋&gt;.&lt;資料&gt;.comment … 主人公のひと言（空行で区切ると順に出る）
    ///   tape/&lt;部屋&gt;.&lt;資料&gt;.&lt;nn&gt; … 音声記録の台詞（nn=00から）
    /// </summary>
    public static class DocCatalog
    {
        private static readonly Color White = Color.white;
        private static readonly Color StickyYellow = new Color(1f, 0.93f, 0.62f);

        private static DocInfo D(InspectKind k, InkStyle ink = InkStyle.Print, float scale = 1f, Color? tint = null, bool audio = false) =>
            new DocInfo { Kind = k, Ink = ink, Scale = scale, Tint = tint ?? White, Audio = audio };

        private static readonly Dictionary<string, DocInfo> Map = new Dictionary<string, DocInfo>
        {
            // 序
            { "dim.news", D(InspectKind.Newspaper) },
            { "dim.tapeplayer", D(InspectKind.TapeRecorder) },
            { "train.ad", D(InspectKind.Poster) },
            { "lab.summary", D(InspectKind.Report) },
            { "lab.members", D(InspectKind.Clipboard) },
            // 1章
            { "study.document", D(InspectKind.Report) },
            { "study.invite", D(InspectKind.Letter) },
            { "study.scrap", D(InspectKind.Sheet, InkStyle.Hand, 0.55f) },
            { "study.photo", D(InspectKind.Photo) },
            { "analysis.idcard", D(InspectKind.Card) },
            { "analysis.plan", D(InspectKind.Report) },
            { "analysis.spec", D(InspectKind.Report) },
            { "analysis.anomaly", D(InspectKind.Monitor, InkStyle.Screen) },
            { "analysis.helmet", D(InspectKind.Helmet) },
            { "analysis.tape", D(InspectKind.Cassette, InkStyle.Hand, audio: true) },
            { "saeki_home.letter", D(InspectKind.Letter, InkStyle.Hand) },
            { "saeki_home.plog", D(InspectKind.Notebook, InkStyle.Hand) },
            { "saeki_home.unsent", D(InspectKind.Sheet, InkStyle.Hand, 0.6f) },
            { "saeki_home.tape", D(InspectKind.Cassette, InkStyle.Hand, audio: true) },
            // 2章
            { "ward.obs", D(InspectKind.Clipboard, InkStyle.Hand) },
            { "ward.girlfile", D(InspectKind.Folder) },
            { "ward.tape", D(InspectKind.Cassette, InkStyle.Hand, audio: true) },
            { "core_ante.manual", D(InspectKind.Report) },
            { "core_ante.brainlist", D(InspectKind.Monitor, InkStyle.Screen) },
            { "core_ante.wmemo", D(InspectKind.Sheet, InkStyle.Hand, 0.6f) },
            { "core_ante.unsentmsg", D(InspectKind.Monitor, InkStyle.Screen) },
            { "mizuno_apart.diary", D(InspectKind.Notebook, InkStyle.Hand) },
            { "mizuno_apart.recorder", D(InspectKind.Recorder, InkStyle.Screen, audio: true) },
            // 3章
            { "data_room.minutes", D(InspectKind.Report) },
            { "data_room.kmemo", D(InspectKind.Sheet, InkStyle.Hand, 0.6f) },
            { "data_room.scribble", D(InspectKind.Sheet, InkStyle.Hand, 0.36f, StickyYellow) },
            { "system_room.devlog", D(InspectKind.Report) },
            { "system_room.gaplog", D(InspectKind.Sheet, InkStyle.Hand) },
            { "system_room.restored", D(InspectKind.Monitor, InkStyle.Screen) },
            { "kuroda_home.rules", D(InspectKind.Notebook, InkStyle.Hand) },
            { "kuroda_home.drawing", D(InspectKind.Drawing, InkStyle.Crayon) },
            { "kuroda_home.lastrec", D(InspectKind.Recorder, InkStyle.Screen, audio: true) },
            { "kuroda_home.tape", D(InspectKind.Cassette, InkStyle.Hand, audio: true) },
            // 終章
            { "core_main.message", D(InspectKind.Monitor, InkStyle.Screen, audio: true) },
            { "son_room.plan", D(InspectKind.Report, InkStyle.Hand) },
            // 黒塗りの資料（穴埋め）と、埋めると開く入れ物の中の資料
            { "lab.guide", D(InspectKind.Sheet) },
            { "lab.visitlog", D(InspectKind.Clipboard) },
            { "saeki_home.diarypage", D(InspectKind.Sheet, InkStyle.Hand, 0.8f) },
            { "saeki_home.envelope", D(InspectKind.Letter, InkStyle.Hand) },
            { "ward.page303", D(InspectKind.Sheet, InkStyle.Hand) },
            { "ward.letter303", D(InspectKind.Letter, InkStyle.Hand) },
            { "kuroda_home.kmemo2", D(InspectKind.Sheet, InkStyle.Hand, 0.6f) },
            { "kuroda_home.letters", D(InspectKind.Letter, InkStyle.Hand) },
        };

        /// <summary>資料の見せ方。一覧に無いもの（おもちゃ・道具など）は実物なし</summary>
        public static DocInfo Get(string roomId, string id) =>
            Map.TryGetValue(roomId + "." + id, out var d) ? d : D(InspectKind.None);

        public static bool IsAudio(string roomId, string id) => Get(roomId, id).Audio;

        /// <summary>
        /// 読む画面に出す本文。メモの文字番号はこの文字列で数えるので、調べる画面と手帳の「引用元」は必ずこれを通す。
        /// 新聞：1行目の日付（「…刊」の行）は読む画面の右上の欄へ分け（dateline）、段落は1字下げて空行で区切る（文章だけを読みやすく）
        /// </summary>
        public static string ReadingBody(string roomId, string id, string body, out string dateline)
        {
            dateline = null;
            body = (body ?? "").Replace("\r", "");
            if (Get(roomId, id).Kind != InspectKind.Newspaper) return body;
            int nl = body.IndexOf('\n');
            string first = (nl >= 0 ? body.Substring(0, nl) : body).Trim();
            if (first.EndsWith("刊"))
            {
                dateline = first;
                body = nl >= 0 ? body.Substring(nl + 1) : "";
            }
            var paras = new List<string>();
            foreach (var p in body.Split(new[] { "\n\n" }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                string s = p.Trim('\n', ' ');
                if (s.Length > 0) paras.Add(s.StartsWith("　") ? s : "　" + s);
            }
            return string.Join("\n\n", paras);
        }

        /// <summary>手帳エントリId（部屋_資料）→ 部屋と資料。部屋Idに _ を含むので部屋の一覧で見分ける</summary>
        public static bool TryParseEntry(string entryId, out string roomId, out string id)
        {
            roomId = null; id = null;
            if (string.IsNullOrEmpty(entryId)) return false;
            string best = null;
            foreach (var r in StoryScript.RoomOrder)
                if (entryId.StartsWith(r + "_") && (best == null || r.Length > best.Length)) best = r;
            if (best == null) return false;
            roomId = best;
            id = entryId.Substring(best.Length + 1);
            return true;
        }

        // ============================== 主人公のひと言（字幕） ==============================

        /// <summary>テキスト表にまだ無い資料のひと言（黒塗りの資料と、その入れ物の中の資料）</summary>
        private static readonly Dictionary<string, string> BuiltInComments = new Dictionary<string, string>
        {
            { "lab.guide", "黒く塗り潰されている。……抜けた言葉は、この部屋のどこかに書いてあるはずだ。" },
            { "lab.visitlog", "目を覚まさない娘……。新聞の一行が、また頭をよぎる。\n\n所長は、なぜこの父親にだけ、ここまでしたんだ。" },
            { "saeki_home.diarypage", "松原の日記だ。……肝心な所だけが、黒く抜け落ちている。" },
            { "saeki_home.envelope", "松原は、最後まで「あの人」の名前を書かなかった。……書けなかったのか。" },
            { "ward.page303", "川崎の観察記録の、続きのページだ。303号……名前が、無い。" },
            { "ward.letter303", "……私は、この人に何をした。" },
            { "kuroda_home.kmemo2", "データ管理室で見たメモの、破り取られた後半だ。……海尾は、何に気づいた。" },
            { "kuroda_home.letters", "「怖いからやめる、は正しくない」。……この人の言葉は、最後まで正しかった。" },
        };

        /// <summary>主人公のひと言。空行で区切られた段落を順に字幕へ出す。無ければ空の配列</summary>
        public static string[] Comment(string roomId, string id)
        {
            BuiltInComments.TryGetValue(roomId + "." + id, out var builtIn);
            string s = GameText.GetMarked($"doc/{roomId}.{id}.comment", builtIn ?? "");   // ルビの印付き（字幕で Ruby を出す）
            if (string.IsNullOrEmpty(s)) return new string[0];
            var parts = s.Replace("\r", "").Split(new[] { "\n\n" }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Replace("\n", "").Trim();
            return parts;
        }

        // ============================== 音声記録 ==============================

        private static readonly Dictionary<string, string> SpeakerNames = new Dictionary<string, string>
        {
            { "saeki", "松原" }, { "mizuno", "川崎" }, { "kuroda", "海尾" }, { "ninomiya", "久賀" }, { "ogawa", "三木" },
        };

        /// <summary>組み込みの台詞（テキスト表が無い時の文面と、話者）。GenAssets/gen_tape_voices.py と同じ並び</summary>
        private static readonly Dictionary<string, (string sp, string text)[]> Tapes = new Dictionary<string, (string, string)[]>
        {
            { "mizuno_apart.recorder", new[] {
                ("mizuno", "松原さん、電話に出ない。海尾さんも会議中。主任も出ない。"),
                ("mizuno", "……決めた。今夜、外部の窓口に全部話す。荷物まとめて、いったん実家に──"),
                ("", "（ドアの開く音）"),
                ("mizuno", "……あれ。どうして、ここが──あ、"),
                ("", "（録音は、そこで終わっている）") } },
            { "kuroda_home.lastrec", new[] {
                ("kuroda", "もう分かっているんです。あなたでしょう。"),
                ("kuroda", "──あなたは娘を救いたいんじゃない。娘を失うことを、受け入れられないだけだ。"),
                ("", "（長い沈黙）"),
                ("kuroda", "……そうか。その顔が、答えか。"),
                ("", "（記録はここで破損している）") } },
            { "core_main.message", new[] {
                ("ogawa", "久賀さん。ここまで来たなら、もう思い出しましたね。"),
                ("ogawa", "あなたは3人に薬を使い、脳を丸ごと写し取って登録した。3人は今も、病棟で眠っています。"),
                ("ogawa", "私はそれに気づきながら、告発しなかった。……私にも、救えなかった息子がいたからです。"),
                ("ogawa", "3人分でも、娘さんは戻らなかった。欠けていた最後のひとつは、『娘さんを知っている記憶』──あなた自身だ。"),
                ("ogawa", "あなたは自ら望み、私が接続した。娘さんに関わる記憶だけを、30%。だからあなたの記憶は、いずれ戻る。3人のようには、ならない。"),
                ("ogawa", "その回廊は、あなたがリナシータと共鳴した時に、あなたの頭の中に生まれたものです。部屋は、あなたと3人の記憶です。私は彼らを消さない。それが私の償いです。"),
                ("ogawa", "最後の工程──どの記憶が本当に娘さんのものかを選り分けることは、父親のあなたにしか、できない。"),
                ("ogawa", "全ての部屋を、もう一度回ってきなさい。") } },
            { "analysis.tape", new[] {
                ("saeki", "補完試験、第12回。記録、松原。"),
                ("saeki", "被験データの再構成で、また同じ現象が出た。本人が経験していないはずの記憶が、補完領域に生成されている。"),
                ("saeki", "生成元を追うと……研究員の脳の癖に似ている。まさか、とは思うが。"),
                ("ninomiya", "松原さん。その補完に使う脳情報は、何人分あれば足りるんですか。"),
                ("saeki", "……久賀さん。録音中です。"),
                ("saeki", "──記録、ここまで。") } },
            { "saeki_home.tape", new[] {
                ("", "（留守番電話の発信音）"),
                ("saeki", "……僕だ。今日も遅くなる。先に寝ていてくれ。"),
                ("saeki", "それと……もし僕に何かあったら、書斎の机を見てくれ。全部、そこに書いてある。"),
                ("saeki", "……いや、何でもない。おやすみ。") } },
            { "ward.tape", new[] {
                ("ninomiya", "……じゃあ、今日も読むよ。『おやすみ』。"),
                ("ninomiya", "おつきさま、おやすみ。まどのそとの、ふくろうも、おやすみ。"),
                ("ninomiya", "……最後のページだ。ほら、みんな眠った。……おやすみ。"),
                ("mizuno", "久賀さん。いま、目が動きました。お父さんの声のときだけ、動くんです。"),
                ("ninomiya", "……そうですか。……そうですか。") } },
            { "kuroda_home.tape", new[] {
                ("", "（留守番電話の発信音）"),
                ("kuroda", "お父さんだ。今日も遅くなる。夕飯は先に食べていなさい。"),
                ("kuroda", "お姉ちゃんは宿題を見てやってくれ。ちびは、歯を磨いてから寝ること。"),
                ("kuroda", "……明日は、大事な話をしてくる。正しいことをしてくるよ。だから、心配しなくていい。") } },
        };

        /// <summary>音声記録の台詞（テキスト表の文面を優先）</summary>
        public static List<TapeLine> Tape(string roomId, string id)
        {
            var list = new List<TapeLine>();
            if (!Tapes.TryGetValue(roomId + "." + id, out var lines)) return list;
            for (int i = 0; i < lines.Length; i++)
            {
                var (sp, text) = lines[i];
                list.Add(new TapeLine
                {
                    Speaker = SpeakerNames.TryGetValue(sp, out var n) ? n : "",
                    Text = GameText.GetMarked($"tape/{roomId}.{id}.{i:00}", text),   // ルビの印付き（字幕で Ruby を出す）
                });
            }
            return list;
        }

        /// <summary>音声記録の書き起こし（読む画面と手帳の本文に使う）</summary>
        public static string Transcript(string roomId, string id)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in Tape(roomId, id))
            {
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(string.IsNullOrEmpty(l.Speaker) ? l.Text : $"{l.Speaker}「{l.Text}」");
            }
            return sb.ToString();
        }
    }
}
