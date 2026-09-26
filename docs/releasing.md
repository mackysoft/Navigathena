# パッケージの公開

## 配布単位

共通本体、Microsoft DI、Unity、uGUI、UI Toolkit、Addressables、VContainer の七つを同じバージョンの NuGet パッケージとして公開する。パッケージ ID は [README](../README.md#配布構成)を参照する。

本体と Microsoft DI は .NET Standard 2.1 の DLL を配布する。Unity に依存する五つは `src/` のソース・アセンブリ定義・`.meta`・表示アセットを `contentFiles/any/any` に格納する。NuGetForUnity が `Sources` へ復元し、Unity がコンパイルする。NuGetForUnity CLI と Editor プラグインは 4.5.0 に固定して検証する。

全パッケージを SDK 標準の `dotnet pack` で生成する。Unity 用の共通定義は `eng/UnitySourcePackage.props` に置く。Unity・Addressables・uGUI・VContainer の実装を再配布せず、外部依存はそれぞれの公式配布元から導入する。Navigathena 自体の UPM パッケージ、Git URL インストール、`.tgz` の生成・公開は行わない。

リリース手順は Yggdrasil・Moira・ucli と同じ `mackysoft/actions` の source-guard、package-state、trusted-publish を使用する。共通 Actions の参照はコミットに固定する。

## NuGet.org の初回設定

`makihiro_dev` でログインし、アカウントメニューの Trusted Publishing から GitHub 用のポリシーを追加する。

| 設定 | 値 |
| --- | --- |
| Policy owner | パッケージの所有者。個人で公開する場合は `makihiro_dev` |
| Repository Owner | `mackysoft` |
| Repository | `Navigathena` |
| Workflow File | `release.yaml` |
| Environment | 空欄 |
| Scopes | 新しいパッケージの公開と既存パッケージの新バージョンの公開 |
| Package glob | `MackySoft.Navigathena*` |

Workflow File には `.github/workflows/` を付けない。このワークフローは GitHub Environment を使用しないため、Environment も指定しない。組織を所有者にする場合は、公開するパッケージの所有者と、ログインユーザーの所属・権限を一致させる。

GitHub の Settings → Secrets and variables → Actions → Variables に、リポジトリ変数 `NUGET_USER=makihiro_dev` を設定する。値は NuGet.org のプロフィール名であり、メールアドレスや GitHub ユーザー名ではない。長期 API キーの Secret は不要。`id-token: write` は公開ジョブだけに付与する。

設定の根拠：[NuGet Trusted Publishing 公式資料](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)。NuGet.org 側のポリシーが未設定の場合は公開できない。

## GitHub 側の Unity 検証

既存の `UNITY_LICENSE`、`UNITY_EMAIL`、`UNITY_PASSWORD` を使用する。Unity 6000.5.5f1 を CI で実行できるライセンスであることを確認する。Secret が存在しても有効なライセンスであることまでは保証されない。

`verify.yaml` は Pull Request、`main` と `2.0` への push、手動実行で検証する。外部 fork の Pull Request には Unity の Secret を渡さず、.NET 検証のみ実行する。取り込む前にメンテナー管理のブランチで Unity 検証も完了させる。`pull_request_target` による未承認コードの実行は行わない。

`release.yaml` は毎回 .NET と Unity を検証し、どちらかが失敗した場合は公開しない。Unity の結果ファイルには、Navigathena のテストが一件以上実行され、すべて成功している必要がある。

## バージョン更新と公開

1. `python3 scripts/set-version.py 2.0.0-preview.1` のようにバージョンを設定する。七つの NuGet と Unity 検証用のバージョンを同時に更新する。
2. `bash scripts/code-quality.sh format` と `bash scripts/verify.sh --unity` を実行する。
3. 変更をレビューし、`main` へマージする。
4. マージ済みコミットに、設定値と同じタグを作成して push する。例：`2.0.0-preview.1`。タグに `v` は付けない。

タグ push が公開の開始操作となる。作業ブランチだけのコミットには公開を許可しない。ワークフローが勝手にバージョンを書き換えたり、コミット・タグを作成したりすることもない。

リリースでは次の順序を守る。

1. タグが `main` に含まれ、ソースのバージョンと一致することを検証する。
2. ビルド・テスト・パッケージ内容・NuGet 利用側の動作を検証する。
3. 七つの NuGet を Unity の独立プロジェクトへ導入し、更新前後の Scene・Prefab の参照と PlayMode の動作をテストする。
4. Trusted Publishing で一時キーを取得し、検証済みの NuGet 成果物を公開する。
5. 公開した七つすべてを再取得し、署名・バージョン・リポジトリコミット・検証済み成果物との内容一致を確認する。
6. 公開済み NuGet を GitHub Release に添付する。プレリリースタグは GitHub でもプレリリースにする。

部分的な公開失敗では同じ実行を再実行できる。`scripts/publication.py` が公開済みの署名と内容を照合してから、未公開分だけを公開用ディレクトリへ用意する。署名・ZIP 管理情報以外に相違がある場合や取得に失敗した場合は停止する。公開済みパッケージを上書きせず、成果物を変更する場合は新しいバージョンにする。

## Unity の復元・更新検証

`scripts/verify.sh` は公開用の七つに加え、同じソースから `0.0.0-upgrade-baseline` のテスト専用パッケージを `upgrade-baseline/` に生成する。これは旧 API との動作比較ではなく、パッケージの格納先がバージョン更新で変わってもアセット参照が維持されることを確かめるためのもの。公開対象には含めない。

`--unity` と CI は次の手順を共有する。

1. 基準版を NuGetForUnity で復元し、DLL・ソース・`.meta`・スタイルシートが配布物と一致することを確認する。
2. EditMode テストで画面の Prefab と Scene を保存する。
3. 保存したアセットを残したまま、公開候補のバージョンへ NuGetForUnity で更新する。
4. 保存済みアセットを再読込し、コンポーネント・Inspector 参照・Resources の読込を検証する。
5. 公開候補から復元したアダプターの表示・入力・演出・寿命管理を PlayMode テストで検証する。

CLI の終了コードだけで成功とせず、各段階で対象のテストアセンブリが実行され、失敗・スキップなしで通っていることを確認する。

## 1.x の扱い

1.x のタグ・GitHub Release は削除しない。1.x を利用し続けるプロジェクトは `1.1.0` などの既存タグに固定する。
2.0 ブランチでは旧 `Assets/MackySoft/MackySoft.Navigathena`、旧 Unity プロジェクト、旧ビルド・テスト・DocFX・unitypackage 公開ワークフローを置き換える。

リポジトリの `package.json` を削除しても、OpenUPM 側にある旧登録 `com.mackysoft.navigathena` は削除されない。2.0 公開前に旧登録が 2.x タグを収集しないよう OpenUPM 側のタグ除外設定を更新し、必要なら説明文の参照先を 1.1.0 に固定する。既存の 1.x タグ・公開済みパッケージは維持する。2.0 の OpenUPM 登録は行わない。
