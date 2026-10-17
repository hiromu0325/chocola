効果音の素材と作り方
====================

■ 出典（すべて CC0 / パブリックドメイン。クレジット表記は不要だが、入れるなら「Kenney (www.kenney.nl)」）
  kenney/kenney_rpg-audio.zip        RPG Audio          https://kenney.nl/assets/rpg-audio
  kenney/kenney_interface-sounds.zip Interface Sounds   https://kenney.nl/assets/interface-sounds
  kenney/kenney_impact-sounds.zip    Impact Sounds      https://kenney.nl/assets/impact-sounds
  （2026-10-17 取得。各 zip 内の License.txt を参照）

■ 作り方
  1. 3つの zip を kenney/ の下に同じ名前のフォルダで展開する
       kenney/kenney_rpg-audio/Audio/*.ogg など
  2. python -I build_sfx.py kenney ../../project/Assets/Audio/Resources/Sfx
     → モノラル化・頭と尻の無音カット・音量そろえをして Sfx/<key>/ に .ogg で書き出す
       （鍵の掛かった扉を揺する音・錠が外れる音・引き出し・電車の引き戸は、素材を重ねたり合成したりして作っている）

■ ゲーム側
  ProceduralAudio.Asset(key) が Resources/Sfx/<key>/ の音から1つ選ぶ（続けて同じ音にはしない、高さを±5%揺らす）。
  フォルダが空・無い時は、従来のその場で合成した音で鳴る。
  音を替えたい時は、そのフォルダの .ogg を差し替えるだけでよい（コードは触らない）。

  key           使う所                                     素材
  door_open     部屋の扉を開ける（回廊・探索者）           rpg doorOpen_1, _2
  door_close    扉が閉まる                                 rpg doorClose_1〜4
  door_latch    ラッチの金属音（カセットを差す等）         rpg metalLatch
  door_rattle   鍵の掛かった扉・入れ物を揺する             rpg metalLatch ×3 ＋ impact woodLight（重ねて作成）
  unlock        錠が外れる（ギミック・扉・黒塗りの入れ物） rpg metalClick ＋ metalLatch（重ねて作成）
  creak         家具の蝶番・隠れ場所                       rpg creak1, creak2
  drawer        木の引き出し                               rpg creak3（低く）＋ impact woodLight
  clunk         ブレイカーのレバー                         impact metalHeavy_002, _004
  switch        懐中電灯・照明のスイッチ                   interface switch_002
  click         ギミック・錠をいじる小さな音、通知         interface click_002, _003
  footstep      探索者の足音                               rpg footstep00〜09
  paper         紙の資料を手に取る                         rpg bookFlip2, 3
  pickup        道具を拾う                                 rpg cloth2, 3
  book_open     手帳を開く                                 rpg bookOpen
  book_close    手帳を閉じる                               rpg bookClose
  page          手帳のページ（資料／メモの切り替え・手帳から資料を開く） rpg bookFlip1〜3
  ui_move       カーソル移動                               interface tick_002
  ui_decide     決定                                       interface select_002
  ui_cancel     戻る・閉じる                               interface back_002
  ui_error      誤り                                       interface error_008
  solve         資料を綴じた・記録した                     interface confirmation_001
  dial_tick     ダイヤルの目盛り                           interface tick_004
  dial_buzz     不正解のブザー                             interface error_006
  train_door_open   電車の車端の引き戸を開ける（かんぬき→戸車のゴロゴロ→戸袋で止まる）  合成の転がり音＋rpg metalClick＋impact metalMedium
  train_door_close  電車の引き戸が閉まる（勢いよく戻って「ガシャン」→かんぬき）          合成の転がり音＋impact metalHeavy・plateHeavy＋rpg metalLatch
  train_door_rattle 鍵の掛かった電車の引き戸を揺する                                       rpg metalLatch ×3＋impact metalMedium
    ※ 転がり音は build_sfx.py の rolling()：帯域を絞ったノイズを戸車の回転でざらつかせ、金属板の響き（impactPlate を畳み込み）を通したもの

  合成音のまま残しているもの（ホラー演出・装置固有の音）:
    悲鳴 / 笑い声 / 話し声 / 鈴 / 警報 / 砂嵐 / 不安のBGM / 停電の予兆 / モニターのビープ /
    ダイヤルの暗証桁の音（順番を音程で示す）/ カセットデッキのボタン
