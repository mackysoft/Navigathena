# Navigathena 2.0

エンジンに依存しない画面管理ライブラリです。履歴、画面のライフサイクル、表示・入力、画面呼び出しと資源の寿命を共通 Runtime が管理し、Unity や DI コンテナーの操作はアダプターへ分離します。

このブランチは 2.0 の公開準備中です。以下のパッケージ構成は 2.0 用であり、1.x の導入手順ではありません。1.x のコードと資料は [1.1.0 タグ](https://github.com/mackysoft/Navigathena/tree/1.1.0)に残っています。

## 配布構成

| 配布 | パッケージ | 用途 |
| --- | --- | --- |
| NuGet | `MackySoft.Navigathena` | 共通 Runtime。必須 |
| NuGet | `MackySoft.Navigathena.MicrosoftDI` | Microsoft DI。必要な場合だけ追加 |
| UPM | `com.mackysoft.navigathena.unity` | Scene・Prefab・表示構成・Animator |
| UPM | `com.mackysoft.navigathena.unity.ugui` | uGUI |
| UPM | `com.mackysoft.navigathena.unity.uitoolkit` | UI Toolkit |
| UPM | `com.mackysoft.navigathena.unity.addressables` | Addressables |
| UPM | `com.mackysoft.navigathena.vcontainer` | VContainer |

共通ライブラリは .NET Standard 2.1、Unity の検証環境は 6000.5.5f1 です。アダプターに共通 Runtime の DLL を同梱しません。VContainer の接続も、別の Host や履歴管理を持ちません。

## Unity への導入

1. [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) を導入し、`MackySoft.Navigathena` をインストールします。Microsoft DI を使う場合だけ、その連携パッケージも追加します。
2. 使用する Unity アダプターを、同じバージョンの GitHub Release の `.tgz` から Unity Package Manager へ追加します。Unity 基本アダプターを先に追加し、必要な UI・Addressables 拡張を追加します。
3. VContainer を使う場合は VContainer 本体を先に導入し、VContainer アダプターを追加します。

Git URL で導入する場合も、バージョンタグを固定します。例の `<version-tag>` は実際に公開されたタグへ置き換えてください。

```text
https://github.com/mackysoft/Navigathena.git?path=/packages/com.mackysoft.navigathena.unity#<version-tag>
```

1.x を導入済みのプロジェクトでは、旧 `com.mackysoft.navigathena` と手動配置した旧 DLL・ソースを削除してから 2.0 を導入してください。旧 API を併存させる移行用ラッパーは提供しません。

## 使い方と設計

- [Unity・DI あり／なしの利用例](docs/30_technical/features/navigathena_usage.md)
- [公開 API と責務](docs/30_technical/features/navigathena_class_design.md)
- [画面の表示構成と演出](docs/30_technical/features/navigathena_screen_presentation.md)
- [設計契約](docs/30_technical/features/navigathena.md)
- [コンパイル・テスト対象の Unity サンプル](tests/Unity/Assets/Samples)
- [公開手順と Trusted Publishing の設定](docs/releasing.md)

`docs/50_tickets` は再設計時の検討記録です。採用済みの使い方は `docs/30_technical/features` と実装を参照してください。

## 開発・検証

.NET SDK 10 と Python 3.9 以降を使用します。

```bash
bash scripts/code-quality.sh format
bash scripts/verify.sh
```

Unity Editor のインストール・ライセンス有効化と Unity CLI の導入後、配布物を使用する Unity テストも実行できます。

```bash
bash scripts/verify.sh --unity
```

この検証は NuGet パッケージと UPM パッケージを作成し、`artifacts/unity-project` に独立した利用プロジェクトを生成します。NuGetForUnity の復元後、配布 DLL との一致と PlayMode テストを検証します。生成先は編集せず、元のテスト・サンプルは `tests/Unity` で管理します。

## ライセンス

[MIT](LICENSE)
