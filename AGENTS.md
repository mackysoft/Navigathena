# 作業規則

- ユーザーには日本語で、略語を重ねず具体的に説明する。
- KISS・YAGNIを守る。画面管理に関係しないゲーム処理をライブラリへ取り込まない。
- 共通 Runtime に Unity、UI フレームワーク、DI コンテナの具体型を持ち込まない。
- テストは公開 API の振る舞いと配布物の利用を検証し、内部構成を固定しない。
- C# の整形は `.editorconfig` と `scripts/code-quality.sh` を正とする。
- GitHub 操作には `gh` を使う。ユーザーの依頼なしに公開・タグ作成・マージを実行しない。

## 構成

- `src/`：NuGet 配布する共通本体、DI 連携、Unity アダプター。
- `eng/`：Unity ソースパッケージに共通の MSBuild 定義。
- `tests/MackySoft.Navigathena.Tests/`：共通 Runtime の振る舞いのテスト。
- `tests/Unity/`：七つの NuGet 配布物を NuGetForUnity で復元する Unity 検証プロジェクトと利用例。外部依存は各配布元の UPM を使用する。
- `docs/`：設計、利用方法、開発・公開手順。

## 検証

```bash
bash scripts/code-quality.sh format
bash scripts/verify.sh
bash scripts/verify.sh --unity
```

Unity Editor と有効なライセンスは実行環境の前提とする。ライセンスの設定をスクリプトで隠蔽しない。パッケージの復元には `scripts/prepare-unity.sh` を使う。
