# -*- coding: utf-8 -*-
"""LoopPrototypeの4部屋＋回廊のコンセプトアートを生成する。

各プロンプトは LoopPrototypeBuilder.cs の実装（什器の配置・照明・寸法）に合わせている。
モデル構成はメモリ comfyui-krea2-recipe に従う（日本語理解＋写実）。
"""
import sys
sys.path.insert(0, r"C:/Users/sdaha/.claude/skills/comfyui-local/scripts")
import comfy

OUT = r"D:/Source/chocola/GenAssets/concept"

STYLE = (
    "ホラーゲームのコンセプトアート、映画的な構図、写実的で緻密な描き込み、"
    "重厚な陰影、静かで不穏な空気、人物なし、文字なし。"
)


def krea2(name, prompt, w, h, seed, steps=8, cfg=1.0,
          unet="redcraft2KREA2RedMix_2Krea2Edition.safetensors"):
    # 導入済みのKrea2系UNETは入れ替わることがある。現存するものを自動で選ぶ
    try:
        avail = comfy.object_info("UNETLoader")["UNETLoader"]["input"]["required"]["unet_name"][0]
        if unet not in avail:
            cand = [u for u in avail if "KREA2" in u.upper() or "Krea2" in u]
            if cand:
                unet = cand[0]
    except Exception:
        pass
    wf = {
        "1": {"class_type": "UNETLoader",
              "inputs": {"unet_name": unet, "weight_dtype": "default"}},
        "2": {"class_type": "CLIPLoader",
              "inputs": {"clip_name": "Huihui-Qwen3-VL-4B-Instruct-abliterated-fp8_scaled.safetensors",
                         "type": "krea2", "device": "default"}},
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": "qwen_image_vae.safetensors"}},
        "4": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["2", 0], "text": prompt + STYLE}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["2", 0], "text": ""}},
        "6": {"class_type": "EmptyLatentImage", "inputs": {"width": w, "height": h, "batch_size": 1}},
        "7": {"class_type": "KSampler",
              "inputs": {"model": ["1", 0], "positive": ["4", 0], "negative": ["5", 0],
                         "latent_image": ["6", 0], "seed": seed, "steps": steps, "cfg": cfg,
                         "sampler_name": "euler", "scheduler": "simple", "denoise": 1.0}},
        "8": {"class_type": "VAEDecode", "inputs": {"samples": ["7", 0], "vae": ["3", 0]}},
        "9": {"class_type": "SaveImage", "inputs": {"images": ["8", 0], "filename_prefix": name}},
    }
    saved, pid = comfy.run(wf, OUT)
    print("->", saved[0])
    return saved[0]


ROOMS = {
    # 6.0×7.5m 天井2.6m。西壁にベッド、東壁に机＋卓上ランプ（唯一の光源）。机上に新聞と懐中電灯
    "concept_dim": (
        "狭く天井の低い薄暗い部屋の内観。左の壁際に古いシングルベッド（灰色の毛布と枕）、"
        "右の壁際に木製の机があり、机の上の小さな卓上ランプが唯一の光源として温かい橙色の光を放っている。"
        "机の上には切り抜かれた新聞記事と金属製の懐中電灯が置かれている。"
        "壁は古びた白い漆喰、床は暗い木の板張り。奥の壁の中央に閉じた扉。"
        "ランプの光が届かない部屋の隅は深い闇に沈んでいる。目覚めた直後の孤独な部屋。",
        1152, 768, 1001),

    # 5.5×7.0m 天井2.9m。中央奥に書き物机と椅子、両壁に高い本棚。机上に文書
    "concept_study": (
        "こぢんまりとした書斎の内観。部屋の中央やや奥に濃い茶色の重厚な書き物机と木の椅子、"
        "左右の壁一面に天井近くまである本棚が並び、古い本がぎっしり詰まっている。"
        "机の上には一枚の古い研究文書が広げられている。"
        "天井の小さな電灯が机を弱く照らし、本棚の影が壁に落ちる。"
        "壁は古い白い漆喰、床は暗い木の板張り。奥の壁に閉じた扉。落ち着いているが息苦しい空間。",
        1152, 768, 1002),

    # 3.0×18.0m 天井3.0m。両側に青いロングシート、吊革、天井から中吊り広告
    "concept_train": (
        "深夜に停車した無人の日本の通勤電車の車内。幅の狭い長い車両の奥まで続く一点透視の構図。"
        "両側に青い布張りのロングシートが並び、天井のレールから白い吊革がずらりと下がっている。"
        "車内の中央、天井から一枚の中吊り広告が吊るされ、紺色の背景に青く光る脳のイラストが描かれている。"
        "蛍光灯は薄暗く不規則に明滅し、窓の外は完全な闇。停止したまま動かない電車。"
        "奥に車両端の扉が小さく見える。",
        1152, 768, 1003),

    # 13×10m 天井3.2m。研究員の机5台（左3右2）、北壁に研究メンバー表のボード（写真4枚うち2枚黒塗り）
    "concept_lab": (
        "広い研究所の応接室兼オフィスの内観。灰色の事務机が左側に3台、右側に2台、島のように並び、"
        "それぞれに書類が散らばっている。奥の壁には研究メンバーの写真を貼った掲示板があり、"
        "4枚の顔写真のうち2枚が薬品で溶けたように黒く滲んでいる。"
        "天井の蛍光灯が均一に白く照らし、清潔だが冷たく無機質。誰もいないのに今まで人がいたような気配。"
        "壁は白、床は暗い木の板張り、奥に閉じた扉。",
        1152, 768, 1004),

    # ロの字回廊。白壁・木の床・片側に扉が等間隔に10枚。窓なし
    "concept_corridor": (
        "終わりのない四角いループ状の廊下の一辺を見た構図。白い壁と暗い木の板張りの床、"
        "内側の壁には同じ形の木の扉が等間隔に何枚も並び、外側の壁には窓が一切ない。"
        "廊下の先は直角に曲がって見えなくなっている。天井の小さな電灯が等間隔に灯り、"
        "その間に暗がりが落ちる。どこまで歩いても同じ景色が繰り返される閉塞感。",
        1152, 768, 1005),
}

if __name__ == "__main__":
    which = sys.argv[1:] or list(ROOMS.keys())
    for k in which:
        p, w, h, seed = ROOMS[k]
        print(k)
        krea2(k, p, w, h, seed)
