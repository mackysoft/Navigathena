---
authority: canon
---

# Navigathena の API と責務

通常利用者は定義、登録、ライフサイクル実装、遷移要求を書く。履歴の確定手順や所有資源の終了順を実装する必要はない。[使い方](navigathena_usage.md)で Unity での呼出し元を示す。

履歴と回答の契約は[履歴復帰](../../50_tickets/technical/navigathena_history_reentry.md)と[画面呼び出し](../../50_tickets/technical/navigathena_screen_calls.md)を参照する。BackOptions と ScreenDefinition.HistoryReturn が再準備・入場演出を制御し、ScreenActivityContext が復帰理由を伝える。IScreenNavigation.InvokeAsync は回答付き Route なら Task<TResult>、通常 Route なら終了を待つ Task を返す。型付き ScreenCall<TResult> が回答または回答なしの終了を要求し、回答のない終了は取消になる。Open による結果通知は公開しない。

ゲームのフローは Presenter や機能の async メソッドが表現する。StartWork はその処理の所有・開始・終了待ちを Runtime へ接続する。ScreenWork.WaitAsync はデリゲート全体の完了を非同期購読などへ伝えるが、ライフサイクル内と自己待機は拒否する。Activity 停止だけでは Work や参照中の View・DI スコープを終了しない。StartWork は明示的に処理全体を画面所有にする任意の入口であり、Invoke の必須条件ではない。直接 Invoke した後の業務処理全体を Runtime が所有するとは扱わない。

## アセンブリの依存

| アセンブリ | 担当 | 依存 |
| --- | --- | --- |
| MackySoft.Navigathena | 共通の定義、カタログ、Host、画面活動、取得所有、ブロッカー、演出、復旧 | .NET Standard 2.1 |
| MackySoft.Navigathena.MicrosoftDI | 実体専用 Provider、登録役割、非同期スコープ破棄 | 共通、Microsoft.Extensions.DependencyInjection |
| MackySoft.Navigathena.VContainer | 親からの子スコープ、標準 Installer、登録役割 | 共通、VContainer。UnityEngine 型は使用しない |
| MackySoft.Navigathena.Unity | Scene、Component、GameObject の取得と消失観測 | 共通、UnityEngine |
| MackySoft.Navigathena.Unity.UIToolkit | UIDocument と UI Toolkit イベントの制御 | 共通、Unity アダプター、UI Toolkit |
| MackySoft.Navigathena.Unity.UGUI | Canvas、GraphicRaycaster、選択中 UI の制御 | 共通、Unity アダプター、uGUI |
| MackySoft.Navigathena.Unity.Addressables | Scene と Prefab アセットの読み込み・解放 | 共通、Unity アダプター、Addressables |

共通 Host に Unity 型や実行先を指定する SynchronizationContext を渡さない。Runtime は独自のスレッドや SynchronizationContext を生成せず、通常の await で実行環境の context を維持する。Unity の構築・遷移・終了はメインスレッドから呼ぶ。競合する構成変更の予約と確定は Runtime が調停するが、汎用処理のスケジューリングは行わない。

## 利用者が使う入口

| 型・API | 責務 |
| --- | --- |
| NavigationDefinition.Build | Region、Route、操作制約、下層への効果を定義 |
| ScreenCatalog.Build | Region と exact Route 型から構築 factory への対応を登録 |
| ScreenDefinition<TRoute> | 安定した構築定義の同一性、Single / Multiple、非同期構築処理 |
| RegisterScreen | Route 型から共有する構築定義、またはその同期選択処理を登録 |
| BlockerDefinition | 一つの共有ブロッカーの構築定義。同じ Host 内で単一実体 |
| RegisterBlocker | その Route で使用する BlockerDefinition の登録 |
| NavigationHost.Create | 資源取得を始めず共通 Host を生成 |
| NavigationHost.Start / StartAsync | 最初の root 構成を同じ遷移機構で開く |
| ScreenActivityContext.Navigation | 現在の画面活動に限定した操作 |
| NavigationHost.Client | アプリケーション側から明示した Region を操作 |
| NavigationHost.State | 不変な現在状態と変更待機 |
| NavigationHost.Terminations | 未完了・失敗した終了の観測 |
| NavigationHost.ShutdownAsync | 受付終了と全所有の終了。全終了時だけ正常完了し、失敗は例外で通知 |
| HistoryTarget | 指定 Region 内の置換開始位置。Current、Unique<TRoute>()、Entry(entryId) |
| ReplaceFrom | 指定した訪問を含む上側の履歴と子構成を、一つの目的地へ置き換える |
| NavigationOptions.RecreateInstance | 目的地と初期子画面の既存実体を流用せず、構築と初期化を行う |
| NavigationException | 実行失敗の元の原因、確定済み履歴、復旧が必要な表示状態 |

Host の名前空間は MackySoft.Navigathena.Hosting。他の通常利用型は MackySoft.Navigathena に置く。Scene 取得拡張は MackySoft.Navigathena.Unity、具体的な UI / Addressables 型は各拡張アセンブリの名前空間に置く。

Host の寿命は外部所有者が決め、アプリケーション全期間にも機能単位にもできる。Root Region の Reset は Host の終了・再生成ではない。新しい Host を作る場合は旧 Host の ShutdownAsync を待つ。終了した Host を再利用せず、借用した外部サービスや Scene の終了はその所有者が判断する。

## 通常操作と実体再生成

PushAsync・ReplaceAsync・ReplaceFromAsync・ResetAsync・BackAsync・ReloadAsync は Task、結果付き InvokeAsync は Task<TResult> を返す。通常遷移の正常完了は遷移成立を保証し、拒否・競合・実行失敗を例外で通知する。NavigationOperation は独立した診断・待機・取消制御が必要な場合の入口に限る。

IScreenNavigation と INavigationClient は要求を受け付ける基本操作を定義する。単独 Route を構成へ変換するオーバーロードは ScreenNavigationExtensions、通常操作の Async メソッドは ScreenNavigationExtensions と NavigationClientExtensions が提供する。実装者は引数変換や取消要求・完了待機を繰り返し実装しない。拡張メソッドも同じ基本操作を通り、活動世代や対象 Region の検証を省略しない。

InvokeAsync は遷移完了ではなく画面終了や回答を待ち、Call の所有と終了条件を必要とするため、インターフェースに残す。Post 操作も活動開始後への要求の予約を伴うため、通常操作の単なる変換として扱わない。

置換の基本操作は ReplaceFrom(HistoryTarget, destination, options) とする。INavigationClient では先頭に対象 RegionInstanceId を渡す。通常の Replace / ReplaceAsync は Current、ReplaceFromAsync<TTarget>(route, ...) は Unique<TTarget>() を渡す拡張メソッドである。

Unique は指定 Region の履歴で Route の実型が一致する一件だけを選ぶ。一致なし・複数一致、別 Region または終了済みの Entry 指定を、画面変更前に拒否する。対象は受付時に識別子へ固定し、実行時に影響範囲の履歴・世代が変わっていれば競合として拒否する。再検索して別の訪問へ置き換えない。Current は従来の自己置換の条件を保ち、背後の画面が別の最上位画面を自分の代わりに置換することを許可しない。

Replace / ReplaceFrom / Reset は新しい訪問を作る。RecreateInstance が true なら新しい実体と画面スコープも作る。Single で旧実体が生存していれば先に終了し、新旧同時存在が必要な演出は変更開始前に拒否する。外部の View・共通サービスの借用を、それらの破棄・再生成へ変えない。

Reload は履歴項目、Route、Call を維持して実体と子実体を再生成する。ReloadOptions.RestoreState で保存表示の適用を指定する。Single は旧実体の終了後に再生成し、回答待ちや Work が保持する実体を無条件に置き換えない。DI スコープも物理実体とともに再生成する。

明示した履歴境界からの ReplaceFrom、RecreateInstance を指定した遷移、Reload は、除去した実体の必須終了まで待つ。外側の置換や Reset で終了する Call は取消になる。存続する Call の回答契約は維持し、終了した呼び出し元を再開しない。

ScreenCatalog.Build の定義 callback は ScreenCatalogDefinitionBuilder を受け取り、Register で Route 定義と画面構築を同時に指定する。RegisterScreens では子 Region の構築や Route ごとのブロッカーを指定できる。定義を確定してから、共通の構築登録・検証へ渡す。

既存の NavigationDefinition に対する Build の callback は ScreenCatalogBuilder を受け取り、RegisterScreens で定義済み Region へ構築方法を登録する。同じ builder に生成方法で変わるモードを持たせず、定義の作成と既存定義への登録を型で区別する。どちらも同じ RegionScreenCatalogBuilder の登録規則で検証し、構築 factory は遷移時まで実行しない。

## 画面ロジック

ライフサイクル処理の公開契約は IScreenLifecycleHandler<TRoute>。ScreenDefinition の構築処理が返す一つの実体を、その画面実体の入口にする。Presenter や ViewModel が直接実装し、別の委譲オブジェクトを要求しない。null の戻り値と、生存中の別画面と同じハンドラー実体の共有は初期化前に拒否する。

```csharp
public interface IScreenLifecycleHandler<in TRoute> where TRoute : Route
{
    ValueTask InitializeAsync(CancellationToken cancellationToken);
    ValueTask PrepareAsync(
        TRoute route,
        ScreenPreparationContext preparation,
        CancellationToken cancellationToken);
    ValueTask ActivateAsync(TRoute route, ScreenActivityContext activity);
    ValueTask DeactivateAsync();
    ValueTask TerminateAsync();
}
```

InitializeAsync は実体生成後に一度、PrepareAsync は表示する履歴項目が変わるたびに呼ぶ。ActivateAsync は同じ項目の活動再開でも Route を受け取る。DeactivateAsync と TerminateAsync は処理の停止完了を表し、オブジェクトの破棄所有とは別である。

| Context | 有効な期間と内容 |
| --- | --- |
| ScreenCreationContext<TRoute> | 構築 callback 内。実体の資源取得、View・Animator・遷移演出、子画面定義の登録。ハンドラーは登録せず構築処理の戻り値にする |
| ScreenPreparationContext | 今回の PrepareAsync 内。EntryId、RegionId、SavedState、今回の表示準備の Resources。登録機能は持たない |
| ScreenActivityContext | 活動世代。EntryId、RegionId、Navigation、CancellationToken |

Route はライフサイクル引数だけから受け取り、DI やコンストラクターから渡さない。表示項目が変わらない活動再開では PrepareAsync を繰り返さない。実体を別項目へ切り替えた後の Back では、戻り先の Route と保存状態で再準備する。

非同期拡張処理を開始してよいかのキャンセル判定は Runtime の責務とする。渡す token を呼び出し直前と正常完了後に確認し、利用者の実装に入口での重複判定を要求しない。実装側は開始後の処理へ token を伝播する。停止・解放処理はキャンセルで省略しない。対象と取消境界は[非同期拡張処理のキャンセル契約](navigathena.md#非同期拡張処理のキャンセル契約)に従う。

ライフサイクル callback 内から Navigation 完了を待たない。イベント処理では PushAsync や InvokeAsync を直接 await する。自分が開いた画面で Activity が停止するため、その活動 token を回答待ちの取消へ渡さない。受理済みの Call は呼び出し元の履歴項目と実体に所有される。

返されたハンドラーが IScreenStateCapture を実装すれば保存を、INavigationChangeHandler を実装すれば保持画面の自己・子構成変更の通知を担当する。別の参加者は登録しない。同じオブジェクトの役割が増えても、所有・破棄を追加しない。

## DI の入口

| API | 呼出し元・責務 |
| --- | --- |
| ScreenDefinition の構築戻り値 | 画面の単一ライフサイクル入口。所有は Resources.CreateOwned や DI スコープ等で別に決まる |
| creation.CreateScope(configure) | 新しい登録集合・Provider・非同期実行スコープを実体へ接続し、指定された単一のハンドラーを返す |
| services.AddScreenLifecycleHandler<T>() | IServiceCollection の定義。画面の入口を一つ指定する。解決は通常の GetRequiredService |
| services.ImportService<T>(provider) | 共通サービスの既存インスタンスを明示的に借用 |
| creation.CreateScope(parent, installer) | 指定した親から子を作り、標準 IInstaller.Install を呼び、指定された単一のハンドラーを返す |
| builder.RegisterScreenLifecycleHandler<T>() | IContainerBuilder の定義。画面の入口を一つ指定する。解決は通常の Resolve |
| BlockerPreparationContext / NavigationTransitionPreparationContext の CreateScope | 演出やブロッカーの構築。Route の表示準備 context には DI 構築 API を提供しない |

表示構成は DI の前に画面取得 API が接続する。
DI は取得済み View とサービスからハンドラーを生成し、View / Animator の個別登録を繰り返さない。

画面の CreateScope は入口の未指定、複数指定、Route 型不一致を拒否する。戻り値をそのまま構築処理から返す。単なるサービス登録はライフサイクル参加を意味しない。

画面専用サービスとライフサイクル実装は、同じ実体のスコープで生存する。Route ごとのスコープは作らない。登録済みの具体型に役割を追加する場合、別の disposable alias を作らない。VContainer のライフサイクル実装は親の登録を暗黙に転用せず、子に登録する。

## 取得と外部所有

```csharp
public interface IResourceAcquisition<T> : IAsyncDisposable
{
    ValueTask<T> AcquireAsync(
        ResourceAcquisitionContext context,
        CancellationToken cancellationToken);
}
```

Resources.CreateOwned は構築するオブジェクトの所有を登録し、通常のコンストラクター factory を呼ぶ。所有オブジェクトの破棄を待ってから、表示準備資源と構築時の取得資源を解放する。DI が解決したオブジェクトをさらに CreateOwned で登録しない。

Resources.AcquireAsync が取得前からこの所有単位を保持する。失敗・取消・通常終了のいずれでも DisposeAsync に到達する。取得途中に内部で作った部分資源も取得実装が解放する。終了失敗では未終了所有を残す。

ResourceAcquisitionContext.ReportLoss は取得アダプターからの消失報告用であり、Navigation を要求する API ではない。Scene や Prefab の native 消失は Unity アダプターがこの入口へ通知する。

外部所有者は ResourceLifetime.Reference で参照を渡す。BorrowAsync は使用関係を登録する。EndAsync が完了する前に外部資源を Destroy / Unload しない。View 登録だけでは外部所有の終了通知を代行しない。

## 表示接続

```csharp
public interface IViewAdapter
{
    object Identity { get; }
    object OrderingDomain { get; }
    bool IsAlive { get; }
    ViewPresentation Presentation { get; }
    event Action<string>? Lost;
    void Validate(ViewPresentation presentation);
    void Apply(ViewPresentation presentation);
}
```

Identity はラッパーの参照ではなく物理 View の同一性、OrderingDomain は表示順を比較できる描画領域を表す。資源実体の取り出しや画面検索には使用しない。

Validate はネイティブ対象の制約と順序の範囲を検証し、表示状態を変更しない。Runtime は確定前にも検証する。Unity 標準アダプターの baseSortingOrder はゲーム側の描画配置設定であり、共通 Host の設定ではない。

ViewPresentation は OutputEnabled、InputEnabled、Order の値である。Adapter はこれをネイティブ機構へ変換し、ゲーム固有の購読や状態遷移は扱わない。登録は同一性を確保してからネイティブ状態を変更する。

画面は取得時に表示構成を一括接続する。
共通の表示構成データは ScreenPresentationBinding、接続用の拡張境界は Integration.ConnectPresentation であり、Unity の InstantiateScreenAsync / LoadScreenAsync / BorrowScreenAsync がこの境界に接続する。
一画面の主たる表示構成は一つとし、複数の表示部品をまとめて検証する。
接続によって取得資源の破棄所有を追加しない。
ブロッカーと遷移全体の演出の準備では RegisterViewAdapter / RegisterExistingViewAdapter を使用する。
後者は起動時オーバーレイなどの現在の出力を維持し、入力だけを閉じる。
詳細は[画面の表示構成と演出](navigathena_screen_presentation.md)に従う。

UiToolkitViewAdapter は SetEnabled を使わずイベント境界と出力用 USS を使用する。入力を閉じる際は当該 View の focus と pointer capture も解除する。UI Toolkit の [pointer capture](https://docs.unity3d.com/cn/6000.0/ScriptReference/UIElements.PointerCaptureHelper.html) は通常の親へのイベント配送と別に扱う必要がある。

CanvasViewAdapter は Selectable.interactable を変更せず Canvas、GraphicRaycaster、EventSystem の選択を扱う。独立した screen-space root Canvas を対象とする。カスタム入力モジュールから閉じた画面へ強制的に選択やイベントを送らない。ゲーム入力は活動ライフサイクルで止める。

## 演出

NavigationTransition は演出を取得する方針であり、演出の実体ではない。Scope は他の遷移との干渉範囲であり、View の所有期間ではない。

| 契約 | 担当 |
| --- | --- |
| INavigationTransitionEffect.BeginAsync | 読み込み前または画面取得後の演出前段階 |
| PrepareSwitchAsync | 確定前の遮蔽・切替準備。同一実体の再準備では旧表示の使用をここまでに終える |
| AfterCommitAsync | 確定後の演出。画面入退場と並行して待機 |
| SettleAsync | Source / Destination / Unavailable に見た目を整える |
| IScreenAnimator | 画面本体の入場・退避・復帰・退出と即時状態反映 |
| IBlockerAnimator | ブロッカーのなしとの間の装飾と即時状態反映 |

演出へ履歴の確定、画面の活動停止、Scene の解放を委譲しない。操作 factory の演出はその Resources.CreateOwned または管理された DI スコープが所有し、操作終了時に解放する。画面所有の演出は画面の同じ所有機構へ登録する。演出のインターフェース自体は破棄を継承しない。SetTransitionEffect で画面に登録した演出は画面の準備から終了まで保持し、再使用する。終了できない演出が使う資源を先に解放しない。

## 内部構成

NavigationRuntime が計画・予約・確定・復旧を調停し、ScreenRuntime が画面の実体と依存を管理する。ScreenInstance が現在の履歴項目との対応、ライフサイクル実装と活動期間、ResourceScope が取得・借用、ViewRegistry が物理表示の同一性、TransitionPlayback が一回の演出使用、BlockerCoordinator が定義単位のブロッカー所有と接続、PresentationOrderCoordinator が物理 View の順序、TerminationJournal が終了の観測を担当する。

CompositionDeriver は親子構成をたどり、同じ Region の下位へ累積した LowerPresentationPolicy を、その項目に属する子構成へ渡す。子 Region の効果を親や兄弟へ戻さない。表示・入力と実体保持の判定は同じ構造導出を使用し、NavigationPlanner に別の全域解放判定を持たせない。Foreground も同じ導出結果から演出へ渡す。

EffectiveComposition は描画順の Presentations と Region ごとの InputBoundaries を持つ。InputBoundary は RegionId と OwnerEntryId を組にし、Host 全体で一つの入力境界を仮定しない。描画順の一列化は重なりの適用に限り、方針の作用範囲には使用しない。

BlockerCoordinator は BlockerDefinition の同一性で実体を保持する。InputBoundaries から使用する定義と接続先を求め、重複する同時接続を検証する。接続を変更する定義を予約し、準備と巻き戻しは今回の接続だけに作用させる。カタログと既定定義は Host、親画面の構築中に登録した定義はその ScreenInstance が所有する。Region の数や履歴項目の削除を実体の生成・終了条件にしない。

PresentationOrderCoordinator は遷移実行層に置き、確定候補の画面順序に、まだ出力中の旧実体、ブロッカー、使用中の演出を加える。各 View を取得時の相対順に並べ、全体で重複しない整数順序を検証・反映する。ViewRegistration は割り当てた順序を保持し、別遷移が古いスナップショットの表示・入力を反映するときに順序を巻き戻さない。View 接続層は画面やブロッカーの選択を行わない。外部 View の取得時の順序は管理接続の解除時に復元する。

公開利用の構成入口は一つの NavigationHost である。内部の presentation transaction を組み立てる API は通常利用へ公開しない。Unity 側にこれらの実行機構を複製しない。
