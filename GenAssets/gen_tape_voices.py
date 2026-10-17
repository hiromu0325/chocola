# -*- coding: utf-8 -*-
"""音声記録（カセットテープ・ボイスレコーダー・端末の再生メッセージ）の台詞をIrodori-TTSで生成する。

方式は gen_echo_voices.py と同じ：人物ごとの基準音声（GenAssets/echo_voice_refs/<人物>.wav）を参照音声にして、
演技キャプション付きで合成する（声質を保ったまま演技だけ変える）。基準音声が無い人物（小川）はここで作る。
台詞の文面は GameText.xlsx の tape/<部屋>.<資料>.<nn> と同じもの（番号＝台詞の順番。物音の行は音声なし）。

出力: project/Assets/Audio/Tapes/<部屋>.<資料>_<nn>.wav
実行: Irodori-TTSのvenvで  python gen_tape_voices.py  [部屋.資料 ...]
"""
import os
import sys

REPO = r"D:\Source\IrodoriTTS\Irodori-TTS"
OUT = r"D:\Source\chocola\project\Assets\Audio\Tapes"
REF_DIR = r"D:\Source\chocola\GenAssets\echo_voice_refs"
sys.path.insert(0, REPO)
os.chdir(REPO)
import infer  # noqa: E402

# 人物の声質（基準音声が無い時だけ使う。既存の人物は gen_echo_voices.py と同じ）
CHARS = {
    "saeki":    ("40代の男性。穏やかで理知的、少し疲れた声。落ち着いてはっきり話す。", 101),
    "kuroda":   ("50代の男性。低く太い声で、厳格で威圧感がある。", 202),
    "mizuno":   ("20代後半の女性。やわらかく優しい声で、少し控えめに話す。", 303),
    "ninomiya": ("40代の男性。疲れ切った低めの声で、感情を抑えて話す。", 404),
    "ogawa":    ("60歳前後の男性。穏やかで静かな低い声。疲れと悔いがにじみ、ゆっくり話す。", 1001),
}
REF_TEXT = "はい、聞こえています。こちらは準備ができました。"

# (人物, 台詞, 演技キャプション)。人物 None は物音の行（音声を作らない。番号は数える）
TAPES = {
    "mizuno_apart.recorder": [
        ("mizuno", "佐伯さん、電話に出ない。黒田さんも会議中。主任も出ない。", "焦って、早口で独り言のように話す。息が浅い。"),
        ("mizuno", "……決めた。今夜、外部の窓口に全部話す。荷物まとめて、いったん実家に──", "震える声で、自分に言い聞かせるように決意を話す。"),
        (None, "（ドアの開く音）", None),
        ("mizuno", "……あれ。どうして、ここが──あ、", "驚いて小さく息をのみ、怯えて言葉が途切れる。"),
        (None, "（録音は、そこで終わっている）", None),
    ],
    "kuroda_home.lastrec": [
        ("kuroda", "もう分かっているんです。あなたでしょう。", "低く静かに、確信をもって問い詰める。"),
        ("kuroda", "──あなたは娘を救いたいんじゃない。娘を失うことを、受け入れられないだけだ。", "厳しく、しかし悲しみを込めてはっきり告げる。"),
        (None, "（長い沈黙）", None),
        ("kuroda", "……そうか。その顔が、答えか。", "静かに、諦めたように小さくつぶやく。"),
        (None, "（記録はここで破損している）", None),
    ],
    "core_main.message": [
        ("ogawa", "二宮さん。ここまで来たなら、もう思い出しましたね。", "穏やかに、静かに語りかける。"),
        ("ogawa", "あなたは3人に薬を使い、脳を丸ごと写し取って登録した。3人は今も、病棟で眠っています。", "感情を抑え、淡々と事実を告げる。"),
        ("ogawa", "私はそれに気づきながら、告発しなかった。……私にも、救えなかった息子がいたからです。", "悔いをにじませ、ゆっくり告白する。"),
        ("ogawa", "3人分でも、娘さんは戻らなかった。欠けていた最後のひとつは、『娘さんを知っている記憶』──あなた自身だ。", "静かに、重く説明する。"),
        ("ogawa", "あなたは自ら望み、私が接続した。娘さんに関わる記憶だけを、30%。だからあなたの記憶は、いずれ戻る。3人のようには、ならない。", "穏やかに、安心させるように話す。"),
        ("ogawa", "その回廊も部屋も、リナシータがあなたと3人の記憶から作った空間です。私は彼らを消さない。それが私の償いです。", "静かに、噛みしめるように話す。"),
        ("ogawa", "最後の工程──どの記憶が本当に娘さんのものかを選り分けることは、父親のあなたにしか、できない。", "真剣に、託すように話す。"),
        ("ogawa", "全ての部屋を、もう一度回ってきなさい。", "静かに、優しく促す。"),
    ],
    "analysis.tape": [
        ("saeki", "補完試験、第12回。記録、佐伯。", "事務的に、落ち着いて口述する。"),
        ("saeki", "被験データの再構成で、また同じ現象が出た。本人が経験していないはずの記憶が、補完領域に生成されている。", "理知的に、少し困惑しながら記録する。"),
        ("saeki", "生成元を追うと……研究員の脳の癖に似ている。まさか、とは思うが。", "声をひそめ、不安げに言葉を選ぶ。"),
        ("ninomiya", "佐伯さん。その補完に使う脳情報は、何人分あれば足りるんですか。", "静かで平坦な声で、淡々と尋ねる。"),
        ("saeki", "……二宮さん。録音中です。", "戸惑って、少し硬い声でたしなめる。"),
        ("saeki", "──記録、ここまで。", "急いで、短く締めくくる。"),
    ],
    "saeki_home.tape": [
        (None, "（留守番電話の発信音）", None),
        ("saeki", "……僕だ。今日も遅くなる。先に寝ていてくれ。", "疲れた声で、優しく家族に話す。"),
        ("saeki", "それと……もし僕に何かあったら、書斎の机を見てくれ。全部、そこに書いてある。", "ためらいながら、真剣に小声で伝える。"),
        ("saeki", "……いや、何でもない。おやすみ。", "取り繕うように、穏やかに笑って話す。"),
    ],
    "ward.tape": [
        ("ninomiya", "……じゃあ、今日も読むよ。『おやすみ』。", "優しく、父親らしい温かい声で話す。"),
        ("ninomiya", "おつきさま、おやすみ。まどのそとの、ふくろうも、おやすみ。", "絵本を読み聞かせるように、ゆっくり柔らかく読む。"),
        ("ninomiya", "……最後のページだ。ほら、みんな眠った。……おやすみ。", "声を落として、ささやくように優しく読む。"),
        ("mizuno", "二宮さん。いま、目が動きました。お父さんの声のときだけ、動くんです。", "嬉しそうに、優しく伝える。"),
        ("ninomiya", "……そうですか。……そうですか。", "こみ上げる感情を抑え、震える声で答える。"),
    ],
    "kuroda_home.tape": [
        (None, "（留守番電話の発信音）", None),
        ("kuroda", "お父さんだ。今日も遅くなる。夕飯は先に食べていなさい。", "父親らしく、穏やかで温かい声で話す。"),
        ("kuroda", "お姉ちゃんは宿題を見てやってくれ。ちびは、歯を磨いてから寝ること。", "少し笑いを含んで、優しく言い聞かせる。"),
        ("kuroda", "……明日は、大事な話をしてくる。正しいことをしてくるよ。だから、心配しなくていい。", "静かに、覚悟を込めて穏やかに話す。"),
    ],
}


def main():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(REF_DIR, exist_ok=True)
    which = sys.argv[1:] or list(TAPES.keys())

    ckpt = infer.download_hf_checkpoint("Aratako/Irodori-TTS-v4.1-Small")
    dev = infer.default_runtime_device()
    runtime = infer.InferenceRuntime.from_key(infer.RuntimeKey(
        checkpoint=ckpt, model_device=dev, codec_repo="Aratako/Semantic-DACVAE-Japanese-32dim",
        model_precision="fp32", codec_device=dev, codec_precision="fp32",
        codec_deterministic_encode=True, codec_deterministic_decode=True,
        compile_model=False, compile_dynamic=False))

    def synth(text, caption, ref_wav, seed, out_path):
        use_spk = ref_wav is not None
        ct, cc, cs, _ = infer.resolve_cfg_scales(
            cfg_guidance_mode="independent", cfg_scale_text=3.0, cfg_scale_caption=3.0,
            cfg_scale_speaker=5.0, cfg_scale=None, use_caption_condition=True,
            use_speaker_condition=use_spk)
        res = runtime.synthesize(infer.SamplingRequest(
            text=text, caption=caption, ref_wav=ref_wav, no_ref=not use_spk,
            num_steps=40, cfg_scale_text=ct, cfg_scale_caption=cc, cfg_scale_speaker=cs,
            cfg_guidance_mode="independent", seed=seed), log_fn=None)
        infer.save_wav(out_path, res.audio, res.sample_rate)
        print("  ->", out_path, flush=True)

    refs = {}
    for name, (caption, seed) in CHARS.items():
        p = os.path.join(REF_DIR, f"{name}.wav")
        if not os.path.exists(p):
            print(f"[ref] {name}: {caption}", flush=True)
            synth(REF_TEXT, caption, None, seed, p)
        refs[name] = p

    for key in which:
        for i, (who, text, act) in enumerate(TAPES[key]):
            if who is None:
                continue
            out = os.path.join(OUT, f"{key}_{i:02d}.wav")
            print(f"[{key} {i}] {who}: {text}  <{act}>", flush=True)
            synth(text, act, refs[who], CHARS[who][1] + 17 * i, out)
    print("done", flush=True)


if __name__ == "__main__":
    main()
