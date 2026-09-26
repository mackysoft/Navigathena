# Navigathena 2.0

エンジンに依存しない画面管理ライブラリです。履歴、画面のライフサイクル、表示・入力、画面呼び出しと資源の寿命を共通 Runtime が管理し、Unity や DI コンテナーの操作はアダプターへ分離します。

このブランチは 2.0 の公開準備中です。以下は 2.0 用の配布構成です。1.x のコードと資料は [1.1.0 タグ](https://github.com/mackysoft/Navigathena/tree/1.1.0)に残っています。

## 配布構成

すべて NuGet で配布し、Unity では NuGetForUnity から導入します。

| パッケージ | 用途 | 内容 |
| --- | --- | --- |
| `MackySoft.Navigathena` | 共通 Runtime。必須 | .NET Standard 2.1 DLL |
| `MackySoft.Navigathena.MicrosoftDI` | Microsoft DI | .NET Standard 2.1 DLL |
| `MackySoft.Navigathena.Unity` | Scene・Prefab・表示構成・Animator | Unity ソース・アセット |
| `MackySoft.Navigathena.Unity.UGUI` | uGUI | Unity ソース・アセット |
| `MackySoft.Navigathena.Unity.UIToolkit` | UI Toolkit | Unity ソース・アセット |
| `MackySoft.Navigathena.Unity.Addressables` | Addressables | Unity ソース・アセット |
| `MackySoft.Navigathena.VContainer` | VContainer | Unity ソース・アセット |

Unity 用のパッケージはソース・アセンブリ定義・`.meta`・必要な表示アセットを保持し、Unity がコンパイルします。共通 Runtime や他社ライブラリの DLL を重複して同梱しません。UPM パッケージや `.tgz` は配布しません。

## Unity への導入

1. [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) を導入します。検証には 4.5.0 を使用しています。
2. Unity Package Manager で、利用する拡張の依存ライブラリを導入します。uGUI・Addressables・VContainer は、それぞれの公式配布元を使用します。UI Toolkit は Unity 組み込みのモジュールを使用します。
3. NuGetForUnity で `MackySoft.Navigathena.Unity` と、必要な UI・Addressables・DI のパッケージをインストールします。共通本体は NuGet の依存関係として導入されます。Navigathena のパッケージは同じバージョンに揃えてください。

Unity の検証環境は 6000.5.5f1 です。外部ライブラリの検証バージョンは [Unity 検証プロジェクトの manifest](tests/Unity/Packages/manifest.json)を参照してください。NuGet が UPM の依存パッケージを代わりに導入するわけではありません。

1.x を導入済みのプロジェクトでは、旧 `com.mackysoft.navigathena` と手動配置した旧 DLL・ソースを削除してから 2.0 を導入してください。2.0 の開発版 UPM アダプターを導入していた場合も、重複コンパイルと GUID の衝突を避けるため、NuGet 版を追加する前に取り除いてください。

## 使い方

- [基本的な利用方法](src/MackySoft.Navigathena/README.md)
- [コンパイル・テスト対象の Unity サンプル](tests/Unity/Assets/Samples)

## 開発・検証

.NET SDK 10 と Python 3.9 以降を使用します。

```bash
bash scripts/code-quality.sh format
bash scripts/verify.sh
```

共通 Runtime と Microsoft DI は、同じ振る舞いテストをプロジェクト参照と配布用 NuGet 参照の両方で実行します。履歴・ライフサイクル・画面呼び出し・資源の終了・失敗時の契約を検証し、別のスモークテストは用意しません。

Unity Editor のインストール・ライセンス有効化と Unity CLI の導入後、配布物を使用する Unity テストも実行できます。

```bash
bash scripts/verify.sh --unity
```

七つの NuGet パッケージを生成し、`artifacts/unity-project` に独立した利用プロジェクトを作ります。NuGetForUnity で復元したアダプターに対して、表示・入力・演出・寿命管理の振る舞いを PlayMode で検証します。配布物の構成と復元内容の一致は、テストではなく配布用スクリプトで確認します。生成先は編集せず、テスト・サンプルは `tests/Unity` で管理します。

## ライセンス

[MIT](LICENSE)
