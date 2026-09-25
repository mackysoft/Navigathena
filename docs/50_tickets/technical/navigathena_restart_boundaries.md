---
authority: proposal
status: implemented
---

# Navigathena：再プレイ・開始地点への遷移・実体再構築

## 目的と適用範囲

画面管理の実装者と利用者に向け、起動基盤と画面の寿命を分離し、開始地点への遷移、ゲームの再プレイ、同じ訪問の再構築を定める。
HistoryTarget、ReplaceFrom、NavigationOptions.RecreateInstance は共通 Runtime に実装している。
現在実装されている公開契約は [使い方](../../30_technical/features/navigathena_usage.md) と [API と責務](../../30_technical/features/navigathena_class_design.md) を参照する。
Route と Presenter の再命名、DI の追加方式、ゲーム進行の実行基盤、一般的な履歴検索言語は対象に含めない。

## 対応する利用パターン

| 利用者の意図 | 変更前から変更後への例 | 操作と終了範囲 |
| --- | --- | --- |
| オプションから画面上の開始地点へ戻る | Home → Options から Boot へ | Root Region の Reset。旧履歴とその子構成を終了する。Boot が旧履歴に存在する必要はない。Root Scene と Host は維持する |
| 共通サービスを含む初期化をやり直す | Host とゲーム共通サービスを終了して起動し直す | ゲームの起動管理が Host.ShutdownAsync を待って外部所有者を終了・再生成する |
| ゲームオーバーから再プレイする | Title → Game① → Result → Confirm から Title → Game②へ | Game①を含む上側の履歴を ReplaceFrom し、目的地を再構築する |
| ゲームオーバー表示が画面の内部部品である | Game①から Game②へ | 現在の訪問を Replace し、目的地を再構築する |
| 新しい条件でゲームを開始する | Game(stage: 1) から Game(stage: 2) へ | 新しい Route を使った Replace。実体も終了したい場合は再構築を指定する |
| 独立した子の進行だけやり直す | 親画面を保持して子 Region を初期構成へ戻す | 対象の子 Region の Reset。親と兄弟 Region は終了しない |
| 同じ訪問の実体だけ作り直す | 訪問 ID と Route を保持して画面を再構築する | 現在の画面の Reload。既存の子履歴を維持する |
| 保持画面で最新データを読み直す | 実体を維持したまま表示データを更新する | ゲームの表示更新、または既存の復帰時 Prepare 設定。Scene の再取得とは区別する |

## 責務と同一性

ゲーム側は、いつ、どこから、どの入力でやり直すかを決める。
共通 Runtime は、履歴、活動、子画面、回答待ち、所有資源、演出、完了と失敗の整合を管理する。
Unity、Addressables、UI、DI の各アダプターは、共通 Runtime が決めた境界で具体的な取得・接続・解放を行う。
Scene 名からゲームの開始地点を推測したり、アダプター側に別の再プレイ手順を実装したりしない。

- Replace / Reset は新しい訪問を作る。同じ Route 値でも旧訪問の ID を再利用しない。
- Reload は同じ訪問を維持し、実体と接続世代を更新する。新しいプレイの開始を表す操作ではない。
- Single / Multiple は同時に生存できる実体数の制約である。Single は再構築禁止、Multiple は再プレイの指定ではない。
- 同じ訪問の維持と、同じ View・Presenter・DI スコープの維持は区別する。

## 起動基盤と画面管理の境界

### Root Scene と Root Region を同一視しない

Root Scene は、ゲーム側がアプリケーションの起動基盤を配置する Unity の Scene である。
Root Region は、NavigationHost が管理する最上位の論理的な画面履歴である。
Root Region の Reset は、その履歴と子画面の変更であり、Root Scene のアンロードや Host の終了を意味しない。

常駐する Root Scene を使うゲームでは、次の所有関係にする。

```text
ゲーム側の起動管理（Root Scene に配置する構成例）
├─ アプリケーション共通サービス・共通 DI コンテナ
├─ 配置済みの共通 View（起動オーバーレイなど）
└─ NavigationHost
   ├─ Host 所有の共有ブロッカー
   └─ Root Region
      └─ Boot / Splash / Title / Game / Popup の履歴と画面実体
         └─ 各画面の子 Region・画面 DI スコープ・所有資源
```

これはゲーム側の構成例であり、ライブラリの必須構成ではない。
DontDestroyOnLoad のオブジェクトや Unity 以外の起動管理からも、同じ NavigationHost と所有契約を使用する。
Root Scene を画面として登録したり、履歴の底に常駐用 Route を置いたりする必要はない。
Root Scene 内に配置された Title の View を借用して画面として扱う場合も、Scene 自体の所有はゲーム側に残る。

### 一度だけ行う起動と、繰り返す画面進行を分ける

| 対象 | 所有・実行する主体 | Boot への Reset |
| --- | --- | --- |
| Root Scene、共通サービス、共通 DI コンテナ | ゲーム側の起動管理 | 維持する。状態を初期化し直すかはゲーム側が決める |
| NavigationHost、画面定義、Host 所有の共有ブロッカー | 起動管理が Host を所有し、Host が管理対象を所有する | 同じ Host を使い続ける |
| Boot / Splash / Title / Game の訪問と画面実体 | 共通 Runtime | 旧履歴と子構成を終了し、新しい訪問を開始する。実体も新しくする場合は再構築を指定する |
| 画面専用の Presenter・DI スコープ・取得した Scene / Prefab | 共通 Runtime と取得・DI アダプター | 画面実体の終了に合わせて停止・解放する |
| Root Scene に配置済みのオーバーレイ View | ゲーム側の外部所有者 | View は維持し、演出による使用だけを開始・終了する |

Host の生成と StartAsync は起動管理で最初に行い、開始後の画面進行をやり直すために再度実行しない。
Boot は戻り先となる画面の意味を表すゲーム側の Route であり、Host やアプリケーションの再生成を指示する特別な Route ではない。
Splash と Title だけを使うゲームなら、それらの Route を開始地点に指定できる。

共通サービスの生成と、ゲーム状態の初期化は同じ処理とは限らない。
同じサービスを維持したままプレイ状態だけを消去する場合は、そのゲーム側の処理を明示的に実行する。
繰り返してよい起動処理は Boot の画面ロジックまたはゲーム側の進行処理に置き、一度だけ行う共通コンテナの構築と混ぜない。
Navigathena はサービスの内容を調べてリセットせず、その処理をどのスレッドで実行するかも決めない。

### 配置済みの共通 View は借用する

ゲーム側の外部所有者が ResourceLifetime と配置済み View の参照を保持し、ResourceReference を構築処理へ渡す。
画面または遷移演出の構築処理は、自分の Resources からその参照を借用する。
借用終了は管理接続と使用の終了であり、View の Destroy や外部 Scene のアンロードではない。

遷移演出は、既に存在する View を借用して演出実装を構築できる。
演出実装の生成と View のインスタンス化を同一視せず、起動時に新しいオーバーレイ Prefab のロードを要求しない。
演出の構築処理が返した実装とその所有資源は当該遷移で管理し、外部 View はその所有者の寿命に従う。
NavigationTransitionScope.Host は表示へ影響する範囲を指定するものであり、演出実装が Host の終了まで生存する指定ではない。
初回の遮蔽済み状態から表示する演出と、通常画面から再び遮蔽して Boot を表示する演出は区別して設定する。
起動基盤を維持することは、初回起動専用の演出を毎回再使用することを意味しない。

外部所有者を実際に終了するときだけ、Host の終了や ResourceLifetime.EndAsync による借用終了を待ってから、View・共通サービス・Scene を破棄する。
通常の Root Region の Reset では外部所有者の終了を要求しない。

## 利用コード

以下の BootRoute、GameRoute、GameplayHudRoute、Regions はゲーム側の型・定義である。
例は通常の入力イベント、または画面のライフサイクル外の進行処理からの要求を示す。
Activity の停止 token は、自身を終了させる遷移の取消 token として自動的に渡さない。

### 最初の起動

```csharp
// ゲーム側の起動管理が保持する host フィールドへ設定する。
// catalog と options は、その起動構成で用意した定義と設定。
host = NavigationHost.Create(catalog, options);
await host.StartAsync(new BootRoute());
```

この生成・起動 API は実装済みである。起動管理を Root Scene の MonoBehaviour に置くか、通常の C# オブジェクトとして DI で構築するかはゲーム側が選ぶ。
Host と共通サービスを画面自身に所有させない。Boot から Title や Game へ遷移しても起動管理は存続する。
起動時のオーバーレイは外部所有の View を使った演出として接続でき、画面履歴の一項目にする必要はない。

### 画面構成を Boot から始め直す

```csharp
var root = activity.Navigation.GetRegionNavigation(RegionTarget.Root);

await root.ResetAsync(
    new BootRoute(),
    new NavigationOptions
    {
        RecreateInstance = true
    });
```

旧 Root Region の履歴とその子画面を終了し、新しい Boot の訪問と実体を構築する。
Root Scene、Host、Host 所有の共通ブロッカー、借用中の外部サービスは、この Reset だけでは終了しない。
StartAsync を再度呼ばず、同じ Host の初期化済みの画面管理を使用する。
以前の Home への Back は成立しない。

画面外のゲーム進行から要求する場合は、同じ操作を既存の Host.Client から発行する。

```csharp
await host.Client.ResetAsync(
    host.Root,
    Destination.For(new BootRoute()),
    new NavigationOptions
    {
        RecreateInstance = true
    });
```

画面からの要求は世代に結び付いた activity.Navigation を使い、外部の起動管理・進行処理は保持する Host.Client を使う。
画面へ生の Host を渡し、終了済み画面からでも構成を変更できるようにする必要はない。

### Game を起点として再プレイする

```csharp
var root = activity.Navigation.GetRegionNavigation(RegionTarget.Root);

await root.ReplaceFromAsync<GameRoute>(
    new GameRoute(),
    new NavigationOptions
    {
        RecreateInstance = true
    });
```

選択した Region の履歴にある GameRoute の訪問を一件特定し、その訪問を含む末尾の範囲を置き換える。
GameOver が内部部品で、Game が現在の画面なら、通常の ReplaceAsync と同じ再構築指定を使えばよい。

```text
変更前: Title → Game① → Result → Confirm
変更後: Title → Game②
```

Result や Confirm を順に Back しない。Game①を途中で再活動させない。
旧 Game の子画面は終了し、新しい Game の子画面は目的地の初期構成から構築する。
再プレイに必要な条件が異なる場合は、新しい GameRoute の引数に渡す。再生成を誘発するためだけの乱数 ID は Route に追加しない。

### 初期子画面も指定する

```csharp
var destination = Destination.For(new GameRoute())
    .Child(Regions.GameUi, Destination.For(new GameplayHudRoute()));

await root.ReplaceFromAsync(
    HistoryTarget.Unique<GameRoute>(),
    destination,
    new NavigationOptions
    {
        RecreateInstance = true
    });
```

GameUi が GameRoute に定義された子 Region である場合の例である。
必要な子 Region の構成検証は既存の Destination の規則を使う。
再プレイを表現するためだけに、既存の一つの履歴を複数 Region へ分割させない。

### 同じ訪問を再構築する

```csharp
await activity.Navigation.ReloadAsync();

// 保存した表示状態も適用する場合だけ指定する。
await activity.Navigation.ReloadAsync(
    new ReloadOptions
    {
        RestoreState = true
    });
```

二つは別々の用途の例であり、連続して呼ぶ処理ではない。
Reload は現在の訪問の Route と子履歴を維持する。保存表示の適用は RestoreState で決める。
子履歴も初期化する再プレイを Reload で代用しない。
回答する側の Reload では、その画面への Call を維持し、新しい実体へ回答権限を接続する。
呼び出し元として未完了の Call や Work が旧実体を使用中なら、現契約どおり事前に拒否する。
同じ訪問を残したまま、参照中の実体を無条件に破棄したり、任意の処理を新実体へ移したりしない。

### 起動基盤自体を終了する場合

Root Scene と共通サービスを維持して Boot へ戻る通常操作とは別である。
共通コンテナや Host 自体を作り直す必要がある場合だけ、ゲーム側の外部所有者がこの終了を管理する。

```csharp
// ゲームの起動管理側で実行する例。
await host.ShutdownAsync();
await externalLifetime.EndAsync();
await applicationServices.DisposeAsync();
await startApplicationAsync();
```

externalLifetime は外部所有資源の借用終了を待つ ResourceLifetime、applicationServices と startApplicationAsync はゲーム側の所有者と起動処理である。
この例では外部 View とその Scene は保持し、再起動側で新しい借用受付を用意する。
外部 View や Scene 自体も破棄する構成では、借用終了後にゲーム側の所有者が破棄・再取得する。
ResourceLifetime.EndAsync は参照先そのものを破棄せず、終了した同じ ResourceLifetime で新たな借用を受け付けない。
終了対象の画面の TerminateAsync から Host.ShutdownAsync を待たない。自分自身の終了待ちになるためである。
Host や借用の終了が失敗した場合は例外で後続を止め、依存サービスの終了や新 Host の起動へ進まない。
常駐 Scene、DontDestroyOnLoad、外部コンテナのどれを使用するかはゲームの構成側が選ぶ。

## 対象を指定する API

対象 Region と、その中の履歴項目の選択を分ける。
IScreenNavigation は既に操作対象 Region と発行元の世代に結び付いている。
GetRegionNavigation で明示的に選択した Region も、この発行元の有効性検証を失わない。

HistoryTarget は副作用のない指定値とし、任意の検索 callback を受け取らない。

| 指定 | 意味 |
| --- | --- |
| `HistoryTarget.Current` | 操作対象 Region の現在の訪問。通常の Replace が使う |
| `HistoryTarget.Unique<TRoute>()` | 同じ Region の履歴から Route の実型が TRoute と一致する一件を指定する |
| `HistoryTarget.Entry(entryId)` | 同じ Region に存在する、特定の訪問を指定する |

Unique は同じ Region の履歴だけを調べ、子・親・兄弟 Region を再帰的に検索しない。
一致がない場合も、複数ある場合も、画面を変更する前に拒否する。
最初または直近の一致へ暗黙にフォールバックしない。Nearest や任意述語の検索は今回追加しない。
正確な一回を選ぶゲーム進行では、その画面の activity.EntryId を所有側が保持して Entry 指定を使える。
識別子は画面実体を保持する権限ではなく、終了した訪問への要求は拒否する。
Scene、GameObject、Route の値の等価性、DI スコープ、ScreenDefinition の単一性から訪問を推測しない。
Current を使う通常の Replace は、既存の自己置換の条件を維持する。背後の画面の処理が、後から最上位になった別画面を自分の代わりに置換してはならない。
明示した履歴境界から置換する要求と、現在の自分を置換する要求を、発行元の検証で区別する。

### 低水準の操作と拡張メソッド

置換の基礎操作は一つにする。以下はインターフェースの公開署名である。

```csharp
// IScreenNavigation
NavigationOperation ReplaceFrom<TRoute>(
    HistoryTarget target,
    NavigationDestinationTree<TRoute> destination,
    NavigationOptions? options = null)
    where TRoute : Route;

// INavigationClient
NavigationOperation ReplaceFrom<TRoute>(
    RegionInstanceId region,
    HistoryTarget target,
    NavigationDestinationTree<TRoute> destination,
    NavigationOptions? options = null)
    where TRoute : Route;
```

基礎操作は ReplaceFrom に統一し、通常利用の Replace / ReplaceAsync は Current を渡す拡張メソッドとする。
`ReplaceFromAsync<TTarget>(Route route, ...)` は `Unique<TTarget>()` を作る拡張メソッドにする。
HistoryTarget と目的地ツリーを受け取る Async 版も拡張メソッドとし、インターフェース実装者へ便利オーバーロードの実装を要求しない。
既存の PostReplace は現在訪問の置換として同じ実行経路へ接続する。
本機能のためだけに Post の検索オーバーロード群、Replay、RestartGame、ReloadScene、汎用バッチ実行器を公開しない。

ReplaceFrom は対象自身を常に除去する。inclusive の真偽で対象を残す操作へ変化させない。
Runtime 内部の操作種別は Replace とし、対象境界を解決した計画を既存の実行系へ渡す。
Reset は対象 Region の全履歴、Back は現在の訪問の除去という意味を維持する。

## 実体の再構築指定

NavigationOptions.RecreateInstance は bool で、既定値は false とする。

- false は再利用可能な実体を使う現在の構築方針に従う。必ず再利用するという保証ではない。
- true は今回の目的地の実体とその初期子構成を既存実体から流用せず、構築処理と初期化を実行する。
- Push / Replace / Reset / 起動、および Invoke の開く遷移に同じ意味で適用する。履歴を変えない処理のフラグにはしない。
- 新しい訪問へ旧訪問の保存状態・旧子履歴を暗黙にコピーしない。入力は目的地の Route と初期子構成で指定する。
- Single は旧実体が安全に終了した後で新しい実体を作る。同時使用が必要な計画を Multiple と偽って成立させない。
- 複数履歴項目が Single 実体を共有していた場合、残す項目の保存状態を保持する。使用中の別項目から実体を奪う計画は事前に拒否する。
- 同じ Host の無関係な実体、共通サービス、資源キャッシュまで解放する指定ではない。

再構築は、構築 callback の再実行と新しい画面スコープを保証する。すべての外部オブジェクトの物理的な作り直しは保証しない。
独立所有の Prefab・Scene は各取得契約に従って解放・再取得する。
配置済み View や共有サービスの借用は、同じ外部実体へ新しい画面スコープから接続できる。
借用元の Scene 自体を再ロードしたい場合は、その Scene の所有者を終了対象に含めるか、外部所有者に再起動を依頼する。
DI なし・Microsoft DI・VContainer は同じ実体再構築契約に従い、画面スコープだけをそれぞれの取得方式で作り直す。

## 除去範囲と呼び出しの扱い

ReplaceFrom の除去範囲は、対象 Region の指定項目から最上位までと、その各項目が所有する子 Region 全体とする。
対象より下の履歴、対象 Region の所有画面、兄弟 Region は範囲外である。
物理資源の借用・依存がある場合も履歴関係へ混ぜず、使用終了と解放順序の検証に使用する。

回答型付き Route は、通常の ReplaceFrom / Reset の目的地へ渡せない。
同じ Call を継続して別画面へ進む操作には Call.Replace / Call.Reset を使う。
呼び出し内から外側の構成を終了する要求は、既存の GetRegionNavigation による明示的な対象選択を必要とする。
除去によって終わる Call を通常の回答成功として完了せず、取消で終了する。旧 Call の ID を新しい訪問へ流用しない。
存続する外側の Call がある場合は、その境界と回答契約を維持する。Call の境界を不整合に切る計画は実行前に拒否する。
除去される呼び出し元を、Call の終了に合わせて再活動させない。

旧実体の終了待ちが、その実体の Call 待ちと循環しないようにする。
不可逆な終了を開始する時点で必要な Call の取消を通知し、待機を解除してから、実体に所有された処理の終了を待つ。
この取消通知だけを、資源解放が完了したという保証にはしない。置換操作の正常完了は必要な終了完了を待つ。
任意のゲーム処理の実行基盤を Runtime へ移さず、ゲーム側の所有者が停止・終了待ちをライフサイクルへ接続する。
自身の終了を待つ Work やライフサイクル callback から、自身を除去する遷移の完了を待つ使い方は許可しない。

## 実行・競合・失敗

1. 発行元の活動・実体世代、対象 Region、Route の許可、目的地の子構成を検証する。
2. HistoryTarget を一つの履歴項目へ解決し、除去範囲と必要な依存・共有表示を予約する。
3. Single、未完了使用、資源依存、演出が要求する同時存在について、登録情報と現在状態から判定できる制約を検証する。
4. 入力を閉じ、必要な活動停止・状態保存・退場・遮蔽を行う。
5. 必要な旧使用を終了する。排他的な資源では旧実体の解放後に、新しい実体を構築・準備する。
6. 一つの履歴変更として確定し、入場・遮蔽解除・活動開始・入力許可を行う。
7. 必須の終了処理も完了させてから、要求の成功を返す。

構築を旧実体の終了より先に実行できる場合は、既存の資源・演出制約に従う。公開 API に新しい手順制御を追加しない。
Game Scene 内の演出 View を、その Scene の解放後も使うことは保証しない。必要なら終了対象外に所有された演出を接続する。
ブロッカーは履歴項目にせず、共通の表示計画で接続先と描画順を切り替える。
Host 所有の共有ブロッカーは Reset / Replace だけで破棄せず、終了対象の画面が所有するブロッカーは依存資源より先に終了する。
演出選択は、実際の遷移元の現在 Route、目的地 Route、Replace / Reset の操作種別から行う。
置換境界を指定した Route を、現在表示している遷移元 Route と同一視しない。

対象は要求受付時に識別子へ固定する。実行待ちや復旧後に同じ検索をやり直して、別の訪問へ対象を変更しない。
予約・実行前の検証では、発行元、対象、影響する履歴と依存世代の変更を検出する。
競合を検出した場合は例外で拒否する。無関係な Region の変更だけで全 Host を停止しない。
一致なし・曖昧な一致・事前に判定できる型や所有制約の違反は、活動や View を変更する前に拒否する。
取得後にしか判定できない Scene・Prefab の構成不備は構築失敗として扱う。外部資源の取得成功まで事前に保証できるとはしない。

Async 版は Task を返す。正常完了は目的地の成立と必須終了の完了を保証し、失敗・拒否は例外で通知する。
必須終了を待つ対象は、Current 以外の明示した履歴境界からの ReplaceFrom、RecreateInstance を指定した遷移、Reload とする。通常遷移で安全に切り離した旧実体は、引き続き Host の終了追跡へ渡せる。
取消可能範囲では取消後の停止・復旧まで待つ。旧 Call の終了や資源解放などの不可逆境界を越えた後は、安全な完了または復旧を待つ。
旧 Scene を既に解放した失敗で、履歴が残っていることだけを理由に旧画面へ復帰したと扱わない。
確定後の失敗は確定先と復旧が必要な実体を報告し、履歴を黙って巻き戻さない。
停止に失敗して依存資源を使用中なら、その資源を先に解放しない。完了済みまたは途中失敗した破棄を無条件に再実行しない。

## 実装と検証

HistoryTarget の解決、除去範囲、再構築指定は共通の遷移計画へ接続する。Unity や DI アダプターへ履歴置換や Host の再起動手順を追加しない。

ScreenRestartContractTests は範囲置換、保存状態、再構築と終了待ち、対象の不一致・曖昧性、準備・解放失敗、Host の維持と再生成を検証する。ScreenCallContractTests は回答待ちの取消と存続する外側の Call を、ScreenDependencyInjectionContractTests は画面スコープと共通サービスの寿命を検証する。
Unity の ScreenScopeUnityTests と ScreenPresentationUnityTests は、配置済み Canvas の借用、Prefab の再生成、Scene の解放・再取得と、DI なし・Microsoft DI・VContainer の接続を検証する。

受け入れ条件は公開操作の外部結果で検証する。

- Home → Options から Root を Boot に Reset し、旧画面へ Back できず、Host 外の借用サービスは生存する。
- 同じ Host で Boot → Title → Game → Boot を繰り返し、Host の再生成・StartAsync の再実行・共通コンテナの再構築を必要としない。
- Root Scene の共通 View を維持しながら画面・演出の借用を終了して再接続でき、Root Region の Reset により View や Scene が破棄されない。
- 初回起動の遮蔽解除と、画面から Boot へ戻る際の遮蔽・解除を、それぞれの演出設定で実行できる。
- Root Scene を前提にしない外部所有者からも同じ Host の開始・Reset・終了を実行でき、Scene 固有の処理は共通 Runtime に入らない。
- Title → Game① → Result → Confirm を Game②へ置換し、Title だけが戻り先に残り、旧 Game は途中で活動しない。
- 内部のゲームオーバー表示から通常の Replace で再プレイできる。
- 子 Region の Reset は親と兄弟を終了せず、親画面の置換ではその子履歴を終了する。
- 型一致なし・複数一致・別 Region の識別子・終了済みの識別子を事前に拒否する。
- 同じ型が二回存在する履歴では、正確な Entry 指定により指定した位置からだけ置換できる。
- 対象解決後の競合で同型の別訪問を置換せず、古い Activity・旧実体からの要求も拒否する。
- 通常の Replace が背後の画面から最前面の別画面を除去せず、明示した境界からの置換と区別される。
- 同じ Route 値の再プレイでも新しい訪問と DI スコープを作り、Single で旧新実体が同時に生存しない。
- 再構築しない通常の単一実体再利用、Back の保存状態復元は引き続き成立する。
- 新しい子構成が旧子履歴を引き継がず、Reload だけは同じ訪問・子履歴を維持する。
- 回答待ちの途中で親 Game を置換しても終了待ちが循環せず、旧回答 callback は新 Game へ作用しない。
- 自身の終了に待機される Work から自己除去の遷移完了を待つ要求を、停止処理との循環に入る前に拒否する。
- 回答側 Reload は Call を維持し、型付き Call.Replace / Call.Reset と外側の構成終了を混同しない。
- 構築・停止・状態保存・解放・準備・確定後の演出・活動開始の失敗で、公開される履歴と資源保持状態が一致する。
- Scene 所有、Prefab 所有、配置済み View 借用で、取得契約に応じた再構築と一回の終了を確認する。
- 画面所有ブロッカー、Host 共通ブロッカー、終了対象外のフェード表示の寿命と描画順を実物で確認する。
- DI なし・Microsoft DI・VContainer で同じ所有・終了・再構築の振る舞いを確認する。
- 共通サービスを終了する前に Host と必要な借用の終了を待ち、終了失敗時に後続の破棄と再起動へ進まない。

実装の完了には .NET の標準検証と Unity の実物テストを含める。
提案の型名や非公開メソッドの配置だけを固定するテストは作らない。

## 判断の参考

[Android Navigation の back stack](https://developer.android.com/guide/navigation/backstack) は、navigate の指定に履歴の除去境界を含める。
[Flutter の pushAndRemoveUntil](https://api.flutter.dev/flutter/widgets/Navigator/pushAndRemoveUntil.html) も、行き先への遷移と既存履歴の除去を一つの操作で扱う。
本案は「除去範囲と新しい行き先を一つの要求に含める」という境界設計を参考にする。
返り値の完了条件、任意述語、同名項目の選び方、資源解放の順序をそのまま採用するものではない。
