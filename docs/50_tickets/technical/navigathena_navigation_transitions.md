---
authority: proposal
status: superseded
---

# Navigathena の遷移演出と完了契約

本書は再設計時の検討記録である。実装済み API の正本は [Navigathena](../../30_technical/features/navigathena.md)、[API と責務](../../30_technical/features/navigathena_class_design.md)、[使い方](../../30_technical/features/navigathena_usage.md) を参照する。本書中の「現行」は再設計前の実装を指す。

## 背景

遷移の演出を利用者が画面管理の前後へ手動で追加すると、活動、取消、入力保護、資源解放との順序が管理から外れる。
演出の選択、演出オブジェクトの生成、View の取得方法、所有期間は、それぞれ別の判断として扱う。
既存の起動用表示、画面の取得後に得られる表示、演出専用に取得する表示を、シーン構成に依存せず接続できることを目的とする。
本書は[アーキテクチャと利用契約](navigathena_architecture.md)の演出部分を具体化する。
演出の選択・進行・取消・完了・使用資源の管理は、エンジン共通の Host と Runtime が実行する。
エンジンや UI を変更してもこれらをアダプターへ再実装せず、資源取得と IViewAdapter による具体的な表示・物理入力操作だけを差し替える。
以下は未実装の公開契約案であり、現在実行できる API の説明ではない。

## 提案

### 利用者が設定するもの

通常の画面は活動 context の Navigation に行き先だけを要求する。
ロード、他画面の停止、黒幕の操作、ブロッカーの切替、終了処理を並べない。

```csharp
activity.Navigation.Push(new InventoryRoute());
```

必要な利用者だけ NavigationOperation.WaitAsync で結果を待つ。
待機取消と遷移取消は[操作の公開契約](navigathena_architecture.md#遷移を要求する-api-と結果の待機)に従う。

| 設定・接続 | 所属と意味 |
| --- | --- |
| NavigationHostOptions.Regions | 共通。RegionDefinitionId ごとの実行設定であり、画面の生成登録とは別 |
| RegionNavigationOptions.Transitions、DefaultTransition | 共通。操作と遷移元・遷移先のルール、および共通の既定値 |
| NavigationOptions.Transition | 共通。その要求の指定。null は方針に委任、None は全体演出を省略 |
| ScreenPresentation | Unity。画面自身の表示・入力アダプターと任意の入場・退避・復帰・退出演出を Inspector で設定 |
| ScreenCreationContext.SetTransitionEffect | 共通。画面実体が保持する全体演出と、その表示先 |
| NavigationTransitionPreparationContext.RegisterViewAdapter | 共通。新たに準備した演出用表示を、非公開の状態から接続 |
| NavigationTransitionPreparationContext.RegisterExistingViewAdapter | 共通。借用済みの既存表示を、現在の表示を維持して接続 |
| Resources.LoadSceneAsync、InstantiatePrefabAsync | Unity アダプターの拡張メソッド。必要な場合の取得と生成 |
| Resources.BorrowAsync | 共通。外部所有者が既に用意した資源を使用するための登録 |

画面のカタログに全体演出の選択方針を置かず、生成 callback の中では取得済み View への接続を行う。
None は画面自身の Animator を無効にせず、演出がない場合も活動、入力保護、所有、取消の契約は変わらない。
ブロッカーは構成に従う入力遮断の表示であり、遷移用の黒幕や履歴項目ではない。

### 演出を選ぶ方針

通常は事前に登録した方針で選び、今回の操作固有の意図がある場合は要求に明示指定する。
操作種別、対象 Region の計画上の遷移元 Route と遷移先 Route を使用し、要求元の Presenter や View の型では選ばない。
Back の遷移先は Runtime が履歴から特定する。
Start は最初の Root 構成を開く操作であり、起動時だけの黒幕解除は要求指定で区別できる。

| 優先順位 | 選択条件 |
| --- | --- |
| 1 | 要求の明示指定。None も選択結果 |
| 2 | 操作種別と、遷移元・遷移先の両方 |
| 3 | 操作種別と、遷移元または遷移先の片側 |
| 4 | Region の操作別既定 |
| 5 | Region の共通 DefaultTransition |
| 6 | 全体演出なし |

同順位で複数の候補が残る設定は曖昧として拒否し、登録順や暗黙の遷移先優先で決めない。
片側同士が競合する場合は、両端の組合せで採用する設定を明示する。
型条件は登録された具体的な Route 型への一致とし、型継承による隠れた優先度を導入しない。
Host の構築時に登録済み Route と操作の組合せを検証し、実行時も計画に対して候補を確定する。

```csharp
var region = new RegionNavigationOptions
{
    DefaultTransition = fade
};

region.Transitions
    .On(NavigationOperationKind.Reset)
    .To<TitleRoute>()
    .Use(NavigationTransition.FromDestinationScreen(
        NavigationTransitionScope.Host));

region.Transitions
    .On(NavigationOperationKind.Replace)
    .From<TitleRoute>()
    .Use(NavigationTransition.FromSourceScreen(
        NavigationTransitionScope.Host));

region.Transitions
    .On(NavigationOperationKind.Push)
    .To<InventoryRoute>()
    .Use(inventoryOpen);

region.Transitions
    .On(NavigationOperationKind.Back)
    .From<InventoryRoute>()
    .Use(inventoryClose);
```

region.Transitions とその登録用 builder は共通 Navigathena の設定 API である。
機能ごとにルールを追加し、組合せを知るアプリケーションの構成側が競合を解決する。
Region の設定は Host 構築時に固定し、子 Region が親の既定値を暗黙に継承する契約にはしない。
初期子構成を含む一操作に、子 Region の全体演出を重ねて自動実行しない。
各画面自身の入退場は、全体演出の選択とは別に必要な対象へ実行する。

選択は副作用の前に、実際の遷移計画に対して一度だけ行う。
選択した設定の Scope と保持要件から影響範囲を予約し、計画を作った状態が変わっていれば実行前に検証し直す。
活動停止、演出生成、画面ロードの開始後に別の設定へ黙って選び直さない。
条件による絞込みと予約は実体の factory を呼ばずに行い、論理確定のロック内でゲーム側コードを呼ばない。
採用した設定、対応した条件、要求指定か既定かを診断へ残す。

Route 引数やゲーム進行による追加判断は、その情報を持つゲーム側の処理から明示指定できる。
通常の型・操作別設定を成立させるために、汎用の条件式や公開の選択器インターフェースを必須にしない。
選択処理の内部拡張と、通常利用者に公開する登録 API は別に判断する。

### 演出の取得先と所有

NavigationTransition は、一回の操作で使う演出の取得先と実行要件を表す不変の指定である。
設定を何度も使うことと、演出オブジェクトや View を再使用することは別である。

```csharp
public sealed class NavigationTransition
{
    public NavigationTransition(
        NavigationTransitionScope scope,
        Func<NavigationTransitionPreparationContext, CancellationToken,
            ValueTask<INavigationTransitionEffect>> createEffectAsync,
        bool requiresSimultaneousScreens = false);

    public static NavigationTransition FromSourceScreen(
        NavigationTransitionScope scope);

    public static NavigationTransition FromDestinationScreen(
        NavigationTransitionScope scope);

    public NavigationTransitionScope Scope { get; }
    public bool RequiresSimultaneousScreens { get; }
    public static NavigationTransition None { get; }
}

public enum NavigationTransitionScope
{
    Region,
    Host
}
```

| 取得先 | 演出実体の所有者 | 実体を使用できる時点 |
| --- | --- | --- |
| createEffectAsync | Runtime の遷移操作 | 演出用資源の取得・借用と factory 完了後 |
| FromSourceScreen | Runtime の遷移元の画面実体 | 対象 Region の遷移元に使用可能な実体があり、演出が接続済みのとき |
| FromDestinationScreen | Runtime の遷移先の画面実体 | 遷移先の準備・初期化が終わり、演出が接続済みのとき。保持中の画面へ戻る場合はその実体を使う |

FromSourceScreen と FromDestinationScreen は、画面の実体や View を事前に要求する API ではなく、今回の遷移計画上のどこから演出を取得するかの指定である。
要求した Presenter や Route 型だけからグローバルに画面を探さず、対象 Region の具体的な Entry と実体世代に結び付ける。
存在しない遷移元を演出のためだけに再生成しない。
選択した画面に演出が接続されていなければ準備失敗とし、別の演出へ黙って選び直さない。

Scope は干渉範囲であり、演出オブジェクトの所有期間ではない。
Host 全体を覆う演出も、一回の遷移が生成・所有できる。
共通 View を使い回すために演出オブジェクトを Host 全体で共用する仕組みは必須にせず、同じ View を借りて一回用の演出を作れる形を標準にする。
画面に接続した演出は、その画面の入退場で同じ実体を使用できる。
使用するたびの取消、結果、表示の整合、排他は遷移実行側の状態として持ち、再生終了を所有元の終了と同一視しない。

### シーンを取得してから演出を接続する

以下はゲーム側の画面生成 callback の抜粋である。
シーン配置済みの View を使う場合に、Bootstrap に未ロードの View を渡させない。

```csharp
var view = await creation.LoadScreenAsync<TitleView>(
    new AddressablesSceneAcquisition(titleSceneReference),
    cancellationToken);

creation.SetTransitionEffect(
    creation.Resources.CreateOwned(() => new TitleTransition(view.TransitionView)),
    view.TransitionViewAdapter);

return creation.Resources.CreateOwned(() => new TitlePresenter(view, titleState));
```

SetTransitionEffect は演出と表示先をその画面実体の所有下へ接続し、再生を開始しない。
演出はこの画面の管理付き取得・借用で得た資源へ依存でき、使用終了より先にそれらを解放しない。
登録後に Presenter の生成や初期化が失敗しても、演出の終了と参照先の解放を Runtime が行う。
同じ演出オブジェクトを複数の画面の所有物として登録することは拒否する。

画面本体と演出の出力を別に接続し、画面を非表示にしただけで演出まで消える構成を許可済みとして扱わない。
共通 Runtime は表示先の利用と順序を調停し、具体的な親子・描画制約の検証は UI アダプターが担当する。
Scene 内の View を使うことは、演出を画面自身の Animator に限定する理由ではない。
同じ取得方法で、全体の黒幕やワイプも接続できる。

### 演出専用の資源を取得する

専用資源が必要な演出は、その生成設定に取得処理を書く。
取得元を選ぶのはゲーム側、具体的な取得・解放はアダプター、所有と終了順序は Runtime の責務である。

```csharp
var fade = new NavigationTransition(
    scope: NavigationTransitionScope.Host,
    createEffectAsync: async (preparation, cancellationToken) =>
    {
        var view = await preparation.Resources.InstantiatePrefabAsync<FadeView>(
            new AddressablesAssetAcquisition(fadePrefabReference),
            cancellationToken);

        preparation.RegisterViewAdapter(view.ViewAdapter);
        return new FadeTransition(view);
    });
```

InstantiatePrefabAsync は指定した元アセットの取得と表示実体の生成を管理し、生成物の使用終了後に元資源を解放する。
元アセットが取得できた後に生成・型解決が失敗した場合も、取得開始前からある所有記録で回収する。
現在のアクティブシーンへ偶然配置されたために予定外のアンロードへ巻き込まれないよう、取得アダプターが所有期間に対応する配置と保持を保証する。
その実現方法として特定の常駐シーンをゲーム側へ要求しない。
画面数に応じた専用 Source クラスや Bootstrap の View 別メンバーは要求しない。

### 起動時の既存オーバーレイを使う

最初のシーンに表示済みのオーバーレイがある場合は、その実体を使用する。
常駐シーンや DontDestroyOnLoad に置くか、起動後に終了するかは外部所有者の構成であり、演出の選択方法と実行契約を変えない。
[Bootstrap の利用例](navigathena_architecture.md#bootstrap-が所有するもの)では、ResourceLifetime から作った型付き参照を演出の factory で借りる。

```csharp
var startupReveal = new NavigationTransition(
    scope: NavigationTransitionScope.Host,
    createEffectAsync: async (preparation, cancellationToken) =>
    {
        var view = await preparation.Resources.BorrowAsync(
            overlay, cancellationToken);
        preparation.RegisterExistingViewAdapter(view.ViewAdapter);
        return new StartupRevealTransition(view);
    });
```

overlay はゲーム側の所有期間が提供する ResourceReference であり、ロードするためのアセット参照ではない。
RegisterExistingViewAdapter は新しい資源を取得せず、表示状態を維持して使用を登録する。
初期表示をいったん消したり、既に覆っている黒幕を再フェードインしたりしない。
起動前からの遮蔽を保ってタイトルを準備し、その後に黒幕を解除して活動・入力を開く。
黒幕のためだけの Route を履歴へ追加せず、StartAsync の後で別途 FadeOut を呼ばせない。

起動用演出の使用終了は、外部所有の View の破棄ではない。
後の遷移でも使用するなら所有元が保持し、終了するなら所有元が EndAsync で使用終了を待ってから具体的に破棄する。
失敗時は可能な範囲で元の遮蔽状態へ整え、使用停止を確認できないまま借用を返さない。
Native の先行消失は正常な返却ではなく障害として扱う。

### 再生の契約と生成可能な時点

実体の取得先にかかわらず、一回の再生は Runtime が所有する。
以下のメソッドは演出の実装者が実装し、通常の呼出し側や Presenter は呼ばない。

```csharp
public interface INavigationTransitionEffect : IAsyncDisposable
{
    ValueTask BeginAsync(
        TransitionBeginContext context, CancellationToken cancellationToken);

    ValueTask PrepareSwitchAsync(
        TransitionTargetsContext context, CancellationToken cancellationToken);

    ValueTask AfterCommitAsync(
        TransitionTargetsContext context, CancellationToken cancellationToken);

    ValueTask SettleAsync(
        TransitionSettlementContext context, CancellationToken cancellationToken);
}
```

BeginAsync は、その演出が参照する資源を利用できる時点で一回だけ呼び、前半演出や表示の初期状態を準備する。
新画面のロード前であることをすべての演出に要求するメソッドではない。
factory 型と遷移元の演出は、必要な取得・借用後、新画面の準備前に BeginAsync を実行する。
遷移先の演出は、新画面の準備・初期化後に解決し、BeginAsync を実行する。
後者にロード前の暗転を要求せず、それが必要ならその時点で存在する別の表示を使用できる指定を選ぶ。
選択した一つの全体演出へ、別の既定演出の前半だけを暗黙に合成しない。

| メソッド | 開始時に成立していること | 演出の処理 |
| --- | --- | --- |
| BeginAsync | 選択した演出実体と参照先が使用可能。入力は保護され、必要な活動停止は完了済み | 再生状態を初期化し、前半演出を行う。既存の起動用黒幕なら表示を保つ |
| PrepareSwitchAsync | 遷移先は準備・初期化済みで論理確定前 | 切替時のマスクなど、演出自身の表示状態を整える |
| AfterCommitAsync | 行き先を論理確定し、入力を閉じたまま表示に参加させる | 黒幕解除などの後半演出を有限の完了まで実行する |
| SettleAsync | 他の再生メソッドを停止・回収済み | 指定した復旧先に応じて演出自身の表示を整える |
| DisposeAsync | 所有元が終了を要求し、その実体の再生・整合処理は終了済み | 実体が直接所有する処理を終了する。借用した View は破棄しない |

TransitionBeginContext は操作と計画上の変更前・変更後の識別、TransitionTargetsContext は旧・新・保持対象の表示変化を読み取り専用で渡す。
一つの From と To だけを前提にせず、下層や子画面を含む複数の参加者を扱う。
これらの context に Native View の探索、キャスト、任意サービスの取得窓口を設けない。
具体的な View は factory または画面の構築で接続済みのものを使う。

TransitionSettlementContext は Source、Destination、Unavailable の復旧先と実際の確定有無を渡す。
Source は遷移開始前の表示、Destination は確定先を見せる表示、Unavailable は利用可能な画面が成立したと偽らない表示を求める。
初期オーバーレイなら成功時は解除状態を保ち、確定前の失敗では元の遮蔽状態へ整える。
登録前の表示を無条件に復元し、成功した起動の黒幕を再び表示する動作にはしない。
履歴、画面の再生成、活動開始、入力開放は Runtime の責務であり、SettleAsync だけで画面の使用可能性を保証しない。

画面所有の演出には再生ごとに DisposeAsync を呼ばず、整合後に使用だけを返す。
取消や失敗後も表示が整い、古い処理が残っていなければ次回使用できる。
整合や停止が失敗した実体を、そのまま次の再生へ渡さない。
遷移所有の演出は再生・整合の後に終了し、その使用終了を確認してから専用資源を解放・外部借用を返却する。
実体の DisposeAsync と、管理付き取得資源の解放を二重に行わない。
任意の共有要素の移動や、ジェスチャーで遷移を巻き戻す汎用 API は本案の対象に含めない。

### 画面自身の入退場

画面自身の入場・退避・復帰・退出と Unity 接続は、[画面の表示構成と演出](../../30_technical/features/navigathena_screen_presentation.md)を正本とする。
画面 Animator と全体演出は異なる演出対象を操作し、完了と停止の調停は共通 Runtime が行う。

### 標準の実行順序

標準は IncomingFirst とし、旧実体を保持したまま新しい構成を準備する。
通常の遷移開始後は、離れる旧画面の操作と活動中処理を止める利用を採る。
そのため、旧画面の活動停止は新画面の準備前とし、演出の有無では変更しない。
長いロード中にも存続すべきゲーム進行や保存は、画面活動ではなくゲーム側の所有期間へ置く。
活動中の旧画面と候補の初期化を並行させる先読み API は、標準の画面遷移へ追加しない。

| 順序 | Runtime の処理 |
| --- | --- |
| 1 | 要求、発行元、登録、遷移計画を検証し、演出設定を選び、実行要件を検証して影響範囲を予約する |
| 2 | 遷移用の物理入力制限を取得し、離れる画面の活動窓口を失効させ、活動取消と DeactivateAsync を待つ |
| 3 | factory 型または遷移元の演出なら、実体を生成・取得して BeginAsync を待つ。遷移先の演出はまだ取得しない |
| 4 | 新画面と必要なブロッカーを取得・生成・初期化する。通常表示と入力にはまだ参加させない |
| 5 | 遷移先の演出ならここで取得して BeginAsync を待つ。描画制約を検証し、画面 Animator の始端と PrepareSwitchAsync を完了する。画面本体はまだ非公開とする |
| 6 | 取消受理と競合しない短い区間で、次の論理構成を一度だけ確定する |
| 7 | 入力を閉じたまま表示構成を反映し、必要な画面の入退場と AfterCommitAsync を並行して待つ |
| 8 | 演出を整えて使用を返す。遷移所有の演出は終了し、旧画面の演出用表示を切り離す。確定した構成の変更通知を配送する |
| 9 | 新たに活動する画面へ新しい ScreenActivityContext を渡し、ActivateAsync の完了を待つ |
| 10 | 表示・活動の成立を確認し、その操作が取得した入力制限だけを解放して結果を確定する |

論理確定の区間内で外部コードの実行や非同期処理を待たない。
具体的な表示反映は論理確定と不可分とは限らず、確定後の反映失敗は明示的な表示障害になる。
停止対象でない画面まで一律に停止・再開しない。
所有のある子画面を終了する場合は子の使用を先に終えるが、親の活動停止だけを子の停止条件にはしない。

演出の生成順は取得先が決めるが、履歴確定、活動、結果の意味は取得先で変えない。
遷移先の演出を後から解決することは、演出設定を選び直すことではない。
画面本体の出力と演出用の出力は別に許可し、論理確定前に新画面の操作や通常表示を漏らさない。

標準では画面入退場と全体演出の切替後段階を並行実行する。
黒幕解除後に画面入場を開始するなど別の演出順は、この標準保証には含めない。
必要性が確認されるまでは、利用者が自由に実行段階を組み替えるパイプライン API は公開しない。
画面演出と資源の寿命をゲーム側で手動調停しないと標準の利用例が成立しない場合は、この設計を見直す。

### 先行解放は資源方針として扱う

OutgoingFirst はメモリや取得条件から選ぶ資源方針であり、演出が勝手に旧画面を破棄する指定ではない。
先行解放を許可する画面では、復元値、所有・借用依存、演出の同時保持条件を資源取得前に検証する。
RequiresSimultaneousScreens と先行解放の組合せは副作用の前に拒否する。
FromSourceScreen は切替後も遷移元の演出と表示を使用するため、その所有元を先行解放する指定とは両立しない。
FromDestinationScreen は解決が新画面の準備後であり、ロード前の遮蔽を提供するものとして検証しない。
この条件は表示資源が共存できない場合を検出するためで、共通 Runtime が Unity のメモリ量を推測する設定ではない。

先行解放では、旧活動停止後に復元値を確保し、旧画面の退場と必要な演出前段階を完了してから旧実体を解放する。
同時保持を必要としない入退場は、旧画面の退場、解放、新画面の取得・入場の順になる。
復元値の取得や依存条件が成立しなければ解放に進まない。
最初の旧実体の解放を始める前に通常の取消受付を閉じる。
これは論理確定とは別の不可逆境界である。

その後の新画面の取得失敗では、保存値から旧構成の再生成を試みる。
同じ View へ戻れることや再生成の成功は保証しない。
復元失敗では DestinationCommitted が false でも RecoveryRequired として、入力保護と未終了所有を残す。
先行解放を選んだことを、解放できない借用元を無視したり、復旧処理を利用側へ丸投げしたりする理由にしない。

### 取消、復旧、完了

IncomingFirst では確定前まで操作取消を受け付ける。
旧活動を停止しただけでは受付を閉じず、正常に停止できた旧画面を新しい活動期間で再開して復帰する。
停止処理自体が失敗した場合は、単に再開を呼んで解決したことにしない。
確定後は要求取消を受け付けず、必要な表示と活動の成立を Host が継続する。

失敗した演出は取消と停止完了を確認してから SettleAsync を呼ぶ。
外部所有者の EndAsync による使用終了要求も Runtime が調停し、確定済みの履歴をその要求だけで巻き戻さない。
元の View がなくなる構成では必要な表示障害を通知し、使えない画面を Ready としない。
論理上の処理終了だけで EndAsync を完了させず、資源を参照する処理と表示の使用終了を待つ。
同じ実体の演出処理、復旧処理、DisposeAsync を並行に呼ばない。
復旧に取消済みの要求 token を使い回さず、Runtime が管理する復旧用の実行条件を使う。
任意のゲーム側アニメーションが必ず正常表示へ戻せるという保証は置かない。
停止が確認できなければ参照する資源を保持し、安全な入力保護と障害状態へ移す。

| 状況 | 結果と処理 |
| --- | --- |
| 検証で拒否 | factory や演出を実行せず、NotEvaluated を返す |
| 確定前の取消・失敗 | 候補を回収し、必要なら旧実体を復元し、Source の演出状態と活動・入力へ戻す |
| 確定後の演出失敗 | 履歴を戻さず Destination の表示へ整える。成功しても CommittedWithFault と診断を残す |
| 確定後の活動開始失敗 | 部分的な活動を停止し、成立しなければ RecoveryRequired とする |
| 正常完了 | 確定、必要な有限演出、活動開始、入力方針まで成立して Ready を返す |
| Host 終了 | 演出を停止・回収し、外部依存への使用終了を確認してから終了成立を返す |

ゲームサービスへの任意の書き込みまで遷移取消で巻き戻す保証はない。
成功した遷移でも、安全に切り離した旧資源の回収が残る場合は、Host が別に終了を追跡する。
遅延した終了失敗で返却済みの結果は変更しない。
未終了の処理が現在の表示へ作用できるなら安全な切離しではなく、正常完了の条件を満たさない。

### 予約と入力保護

Region 内で完結する操作は独立 Region の遷移を一律に止めない。
Host 全体を覆う黒幕は Host 範囲を宣言し、演出生成前にその干渉範囲を予約する。
共有表示の制御も排他的に登録し、同じ native View を複数の演出が同時に制御しない。
依存資源としての借用は共有できても、表示の書込み権限は別の予約として扱う。
演出の実体自体にも再生の排他を持ち、画面所有の同じ実体を二つの操作が同時に再生しない。
登録しない外部 View へ直接書き込む演出まで Runtime が発見・保護できるとは保証しない。
重なる操作は Conflict とし、暗黙の待機列や実行中の演出の強制打切りを追加しない。

入力制限は、遷移、構成上の遮断、障害の各理由に対応して保持する。
一つの操作の終了で、別の操作やモーダル、障害が保持する入力制限を解放しない。
入力遮断は request の競合検証の代わりにならず、競合検証もゲーム側の活動停止の代わりにならない。
UI アダプターは接続した物理入力経路を保護し、画面側は活動中の購読を終了する。
ブロッカーの見た目が失われても、この保護は失わない。

演出用の予約は必要な表示・活動・入力の成立まで保持する。
安全に切り離した終了処理の資源使用権は、操作の予約と分けて終了完了まで保持する。
短い論理確定の排他を、演出の全期間まで延ばさない。

### 戻る際の選択と進捗

Back も今回の操作種別・遷移元・遷移先で演出を選ぶ。
開くときの全体演出設定を履歴へ保存して戻る際の既定にする契約にはしない。
今回限りの要求指定を後の Back へ暗黙に持ち越さず、開閉で同じ設定を使う場合は両方のルールで指定する。
画面所有の演出実体を保持することは、遷移全体の選択設定を履歴へ保存することとは異なる。
戻る画面の実体が再生成される場合は、その世代に接続した演出を使用し、終了した View や token を再使用しない。
自動的な逆再生や、起動時だけのオーバーレイへの参照の再使用は行わない。

ローディング演出は任意で INavigationProgressReceiver を実装できる。
進捗配送は表示情報であり、演出や画面の寿命を進める指示ではない。
ゲーム固有の進捗報告の生成 API は本案では追加せず、現行の段階通知を超える能力が実装済みとは扱わない。

## 判断材料

### 現行からの変更と根拠

旧版の Scene 遷移は、初期化、Director の End、OnEnter を順に待つ。
活動前に必要な演出を待つ利用は維持するが、旧実装の障害処理がすべて正しいという意味ではない。

現行 Core の [ExecutePublicationAsync](../../../src/MackySoft.Navigathena/Runtime/Execution/NavigationRuntime.cs) は短い確定区間の外で CompleteAsync を待つ。
既存の予約も ExecuteAsync の終了まで保持される。
この土台は利用できるが、標準の演出、活動 context、演出用の一時表示が実装済みという意味ではない。

遷移・構築 API の設計案は、検証済み実装とは区別する。
公開前の開始状態、取得前所有、実行と待機の分離を採り、演出前の活動開始や Host 全体の一律直列化は採らない。
段階の数を理由に利用者向けの汎用パイプラインを作らず、標準利用で必要な順序に契約を限定する。
設定選択、画面所有の演出の解決、外部資源の使用終了、新たな BeginAsync は未実装の設計であり、現行コードの呼出し順がそのまま証拠になるとは扱わない。

### 採否に必要な外部動作の実証

| 利用条件 | 確認する外部動作 |
| --- | --- |
| 共通の演出実行 | 同じ共通 Host と Runtime で取得・View アダプターだけを差し替え、演出の選択、呼出し順序、取消、復旧、終了が成立する |
| 異なるゲーム構成 | 常駐シーン、DontDestroyOnLoad、個別に終了する所有元で、同じ演出登録・起動・終了が成立する |
| 起動前から存在する表示 | 新規ロードや複製をせず同じ View を使い、登録時に遮蔽が途切れない。成功時に解除し、確定前の失敗では遮蔽を保つ |
| 遷移先のシーン配置 View | View の取得後に演出を作れる。BeginAsync を取得前に呼ばず、画面本体を早期公開しない |
| 画面所有の演出の再使用 | 入退場で同じ演出を使用でき、再生終了ごとに Dispose しない。画面の終了では参照中処理より先に資源を解放しない |
| 専用資源と既存資源 | factory が取得途中で失敗しても回収する。借用した View と所有元は演出終了で破棄しない |
| 所有元の先行終了 | 準備中・再生中・停止失敗中に EndAsync を要求し、資源を参照する処理が残ったまま完了しない |
| 演出選択の競合 | 同順位の競合は登録順で決まらず、明示した両端条件で解消できる。None は既定へ戻らない |
| Back の演出 | 開くときの一回限りの指定が持ち越されず、Back のルールで選ばれる |
| 制御の競合と世代 | 異なる wrapper や参照から同じ表示を同時制御できず、再生成後に旧 callback が新しい View を操作しない |
| 起動黒幕から Title | 二度目の暗転がなく、解除と必要な入場演出の前に操作購読が始まらない |
| 演出なし | 通常の停止・準備・活動・取消の意味が演出追加時と一致する |
| 準備中の表示 | Scene 有効化から公開まで表示と入力が漏れず、初期化を遅延させても一瞬表示されない |
| ポップアップの開閉 | 下層を見せたまま購読を止め、退場完了後に一回だけ再開する |
| 交差フェード | 旧 View と新 View が必要な期間共存し、旧画面の活動窓口は無効のまま |
| 先行解放と同時保持演出 | 不整合を副作用の前に拒否する |
| 先行解放後の取得失敗 | 同じ実体へ戻ったと偽らず、再生成失敗を RecoveryRequired として扱う |
| 確定前の取消 | 元の黒幕と画面へ戻り、新しい活動期間で購読が一重に再開する |
| 待機だけの取消 | 操作は完了し、他の待機者と Host は同じ終端結果を受け取る |
| 確定後の演出障害 | 確定を戻さず、表示復旧の成否と演出障害を別々に観測できる |
| 独立 Region と共有黒幕 | 局所遷移は並行し、全体黒幕との競合は演出生成前に拒否する |
| 入力制限の重なり | 一つの遷移完了で別の遮断理由が解除されない |
| 遅延回収 | 正常結果後も資源所有が追跡され、使用中の設備を次の操作へ渡さない |
| Host 終了 | 演出が使う View を先に解放せず、終了成立後に古い処理が外部へ作用しない |

共通 Host と Runtime の実行テストと、各 UI・取得アダプターの実機接続テストを分ける。
各アダプターへ遷移の状態機械、寿命の調停、起動・終了管理の実装を要求する場合は、責務境界が成立していないものとする。
API 宣言だけのコンパイルは動作実証の代わりにしない。
旧画面の一時表示、公開前の隔離、入力制限の独立した保持、標準の演出順は実装による確認が必要である。
標準利用に画面側の手動の寿命調停が必要になる場合、または独立した Region を一律停止しなければ成立しない場合は、契約か実現方式を見直す。

## 回答

## 関連文書

- [アーキテクチャと利用契約](navigathena_architecture.md)
- [現行の設計契約](../../30_technical/features/navigathena.md)
- [現行の利用例](../../30_technical/features/navigathena_usage.md)
