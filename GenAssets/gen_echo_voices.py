# -*- coding: utf-8 -*-
"""残響（記憶の残滓）の会話をIrodori-TTSで生成する。

方式:
  1. 人物ごとに VoiceDesign（--no-ref + キャプション + 固定seed）で「基準音声」を1本作る
  2. 各台詞は基準音声を参照音声にして、感情キャプション付きで合成（声質を保ったまま演技を変える）
視点差ペア（argue_* / talk_*）は同じ台詞を違う演技指示で作る＝すれ違い表現の核。

出力: project/Assets/Audio/Echo/<echoId>_<nn>.wav
実行: Irodori-TTSのvenvで  python gen_echo_voices.py  [echoId ...]
"""
import os
import sys

REPO = r"D:\Source\IrodoriTTS\Irodori-TTS"
OUT = r"D:\Source\chocola\project\Assets\Audio\Echo"
REF_DIR = r"D:\Source\chocola\GenAssets\echo_voice_refs"
sys.path.insert(0, REPO)
os.chdir(REPO)
import infer  # noqa: E402  (infer.py がランタイムAPIを全部importしている)

# ---- 人物の声質（基準音声のキャプションとseed）----
CHARS = {
    "saeki":     ("40代の男性。穏やかで理知的、少し疲れた声。落ち着いてはっきり話す。", 101),
    "kuroda":    ("50代の男性。低く太い声で、厳格で威圧感がある。", 202),
    "mizuno":    ("20代後半の女性。やわらかく優しい声で、少し控えめに話す。", 303),
    "ninomiya":  ("40代の男性。疲れ切った低めの声で、感情を抑えて話す。", 404),
    "wife":      ("40代の女性。落ち着いた家庭的な声。", 505),
    "girl":      ("小学生の女の子。元気で幼い声。", 606),
    "boy":       ("小学生の男の子。幼くて無邪気な声。", 707),
    "mother":    ("40代の女性。明るく優しい声。", 808),
    "girl2":     ("小学校低学年の女の子。少し甘えた幼い声。", 909),
}
REF_TEXT = "はい、聞こえています。こちらは準備ができました。"

# ---- 台詞（echoId, 人物, 台詞, 演技キャプション）。順番がファイル番号になる ----
A1 = "佐伯くん。システムは止めるべきだ。もう、治療とは呼べない。"
A2 = "分かっています。ですが、今止めれば……あの人は。"
A3 = "あの人？　誰の話をしている。"
A4 = "いえ。もう少しだけ、時間をください。"
T1 = "水野さん。提供体は……どこから来るんですか。"
T2 = "それは……私たちが決めることでは、ないと思います。"
T3 = "娘を、助けたいんです。"
T4 = "……はい。分かっています。"

LINES = {
    # 対A 口論：佐伯の記憶＝冷静な対話
    "argue_saeki": [
        ("kuroda", A1, "落ち着いた低い声で、静かに諭すように話す。"),
        ("saeki",  A2, "穏やかに、言葉を選びながら丁寧に話す。"),
        ("kuroda", A3, "静かに、探るように問いかける。"),
        ("saeki",  A4, "落ち着いて、誠実に頼み込む。"),
    ],
    # 対A 口論：黒田の記憶＝一方的に怒鳴られている（同じ台詞）
    "argue_kuroda": [
        ("kuroda", A1, "弱々しく、遠慮がちに小さな声で話す。"),
        ("saeki",  A2, "激しく怒鳴りつけるように、荒々しく叫ぶ。"),
        ("kuroda", A3, "怯えて声が震え、おどおどと尋ねる。"),
        ("saeki",  A4, "苛立ちを爆発させ、威圧的に怒鳴る。"),
    ],
    # 対B 最後の会話：主人公の記憶＝穏やかな場面
    "talk_me": [
        ("ninomiya", T1, "穏やかに、静かに尋ねる。"),
        ("mizuno",   T2, "優しく、少し困ったように答える。"),
        ("ninomiya", T3, "切実に、しかし穏やかに訴える。"),
        ("mizuno",   T4, "温かく、微笑むように答える。"),
    ],
    # 対B 最後の会話：水野の記憶＝顔のない影に詰め寄られる（同じ台詞）
    "talk_mizuno": [
        ("ninomiya", T1, "低く平坦な声で、感情なく詰め寄るように話す。"),
        ("mizuno",   T2, "怯えて声が震え、言葉に詰まりながら話す。"),
        ("ninomiya", T3, "低く、抑揚のない不気味な声で話す。"),
        ("mizuno",   T4, "泣きそうな声で、小さく答える。"),
    ],
    # 対C 二人分の食器（佐伯の妻）
    "saeki_wife": [
        ("wife", "恒一さん、コーヒー入ったよ。", "家庭的に、優しく呼びかける。"),
        ("wife", "……今日は、早く帰ってきてね。", "少し寂しそうに、小さな声でつぶやく。"),
    ],
    # 対E 叔父の病室（幼い水野）
    "mizuno_uncle": [
        ("girl", "おじさん、今日ね、学校で発表したんだよ。", "元気に、嬉しそうに話しかける。"),
        ("girl", "……また明日も来るからね。おやすみ。", "静かに、少し寂しそうに話す。"),
    ],
    # 対D 食卓（黒田の家族）
    "kuroda_family": [
        ("boy",    "おとうさん、まだ帰ってこないの？", "無邪気に、不思議そうに尋ねる。"),
        ("mother", "もうすぐ帰るって。先に食べちゃおうか。", "明るく、優しく話す。"),
        ("girl2",  "やだ、まってる！", "元気に、少し拗ねて言う。"),
    ],
}


def main():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(REF_DIR, exist_ok=True)
    which = sys.argv[1:] or list(LINES.keys())

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

    # 1) 基準音声（人物ごと。既にあれば再利用＝声質が固定される）
    refs = {}
    for name, (caption, seed) in CHARS.items():
        p = os.path.join(REF_DIR, f"{name}.wav")
        if not os.path.exists(p):
            print(f"[ref] {name}: {caption}", flush=True)
            synth(REF_TEXT, caption, None, seed, p)
        refs[name] = p

    # 2) 台詞
    for echo in which:
        for i, (who, text, act) in enumerate(LINES[echo], start=1):
            out = os.path.join(OUT, f"{echo}_{i:02d}.wav")
            print(f"[{echo} {i}] {who}: {text}  <{act}>", flush=True)
            synth(text, act, refs[who], CHARS[who][1] + i, out)
    print("done", flush=True)


if __name__ == "__main__":
    main()
