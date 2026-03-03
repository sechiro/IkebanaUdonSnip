# IkebanaUdonSnip

VRChat World向けの、いけばな花材加工に特化したUdonSharpメッシュカットギミックです。

## 含まれる内容
- `Editor/`: セットアップ用Editor拡張
- `Runtime/`: 実行時UdonSharpスクリプト群
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

## セットアップ（概要）
1. `Editor` と `Runtime` をUnityプロジェクトへ配置
2. `Hatago/Ikebana/Open Cutter Setup Tool` を開く
3. CutTargetを指定してセットアップを生成

## 基本操作
1. `ScissorObject` をPickupし、対象に接触した状態で `Use` してカット
2. `UndoButtonCube` の `Use` 長押し（1秒）でUNDO
3. `ResetButtonCube` の `Use` 長押し（1秒）でReset

## 既知の制約
- 高ポリモデルでは負荷が高くなる場合があります。
- UNDO履歴のグローバル共有は未実装です（現仕様: カット実行者本人のみUNDO可能）。

## License
MIT (see [LICENSE](./LICENSE))

