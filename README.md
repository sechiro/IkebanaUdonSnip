# IkebanaUdonSnip

VRChat World向けの、いけばな花材加工に特化したUdonSharpメッシュカットギミックです。

## 含まれる内容
- `Editor/`: セットアップ用Editor拡張
- `Runtime/`: 実行時UdonSharpスクリプト群
- `resources/sound/scissors1.mp3`: カット効果音
- `LICENSE`: MIT License

## 含まれない内容
- 内部向け設計資料・運用メモ（`docs/`）
- 開発ルールファイル（`AGENTS.md`）

## 依存関係
- Unity `2022.3.22f1`
- VRChat SDK (World) `3.10.0+`
- UdonSharp（VCC安定版）
- TextMeshPro
- Fukuro Udon (`com.mimylab.fukuroudon`)
  - `ManualObjectSync`
  - `PickupPlatformOverride`

## セットアップ
1. `Editor`、`Runtime`、`resources` をUnityプロジェクトへ配置
2. `Hatago/Ikebana/Open Cutter Setup Tool` を開く
3. CutTargetを指定してセットアップを生成
4. 生成されたハサミの `CutTrigger` にある `IkebanaSnipContactPicker` の `Cut Audio Clip` に `resources/sound/scissors1.mp3` をアサイン

## 基本操作
1. `ScissorObject` をPickupし、対象に接触した状態で `Use` してカット
2. `ScissorObject` を手放すと真下に `UndoButtonCube` が出現
3. `UndoButtonCube` を見ながら `Use` 長押し（1秒）で直近カットをUNDO
4. `ResetButtonCube` を見ながら `Use` 長押し（1秒）でSetup全体をリセット

## 複数のCutTargetをハサミ1本で共有する場合

`IkebanaSnipContactPicker` の `Max Tracked Cutters` を次の値以上に設定してください。

> Max Tracked Cutters >= CutTarget数 × (2^カット深度 - 1)
>
> 例: CutTarget 14個・カット深度 5 の場合 → 14 × 31 = 434 → **512** を推奨

デフォルト値（64）はCutTarget 2個以下を想定した値です。超過するとカットが無反応になります。

`Max Registered Root Cutters` は CutTarget数以上に設定してください（デフォルト: 16）。

## 既知の制約
- 高ポリモデルでは負荷が高くなる場合があります。
- UNDO履歴のグローバル共有は未実装です（現仕様: カット実行者本人のみUNDO可能）。

## クレジット
効果音素材: 小森平 (https://taira-komori.net/)

## License
MIT (see [LICENSE](./LICENSE))
※ただし、効果音を除く

