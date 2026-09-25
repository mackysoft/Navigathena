---
authority: proposal
status: superseded
---

# Navigathena のアーキテクチャと利用契約

本書は再設計時の検討記録である。実装済み API の正本は [Navigathena](../../30_technical/features/navigathena.md)、[API と責務](../../30_technical/features/navigathena_class_design.md)、[使い方](../../30_technical/features/navigathena_usage.md) を参照する。本書中の「現行」は再設計前の実装を指す。

## 背景

ブロッカーは、画面の積み重ねに対して下層への入力を遮断するものである。
下層を見せたまま重なる非フル画面の Route が、下層への入力を止めるために使用する。
フル画面の Route はブロッカーを使用せず、その画面に覆われた下層のブロッカーも表示しない。
画面の論理的な入力可否と、ブロッカーの View の表示は別に判断する。
画面の実装者は、その画面のライフサイクルに応じて、画面固有のロジックを開始・停止できる必要がある。
対象は操作入力だけでなく、イベントの購読、更新処理、その画面が活動している間だけ必要な処理を含む。
通知の受け手はゲーム側の画面制御オブジェクトとし、View や UI フレームワークの component にロジックの所有を強制しない。
入力を止めるために UI 部品の有効状態や見た目を変更することを必須にしない。
共通ブロッカーを一つ設定すれば、複数の画面と遷移で同じ実体を使い回せる。
必要な画面だけ、共通ブロッカーに代わるカスタム実装を指定できる。
ゲームは、そのブロッカーに画面固有の見た目、操作説明、内部構成、アニメーションを実装できる必要がある。
ブロッカー同士の切替は即時とし、ブロッカーがない状態からの登場と、最後のブロッカーの退場だけを演出の対象とする。
カスタム実装でも、その生成、準備、遮断、表示切替、保持、復旧、終了のサイクルは Navigathena が管理する。
画面の実装者に、そのサイクルの呼出しや後始末を委ねない。
Unity 自体を選択可能なエンジンアダプターとし、共通の画面管理は Unity がなくても利用できるものとする。
UI Toolkit は Unity 上で選択する UI アダプターとし、共通 API はエンジンと UI フレームワークの型を要求しない。

既存の表示を使う起動、遷移先シーンの View を使う演出、専用資源を取得する演出を、同じ画面管理へ接続できる必要がある。
起動用オーバーレイを最初のシーンへ配置する利用では、表示するための後続ロードや Prefab 生成を要求しない。
常駐シーン、DontDestroyOnLoad、個別オブジェクトの生成・破棄はゲーム側の構成であり、いずれかをライブラリの前提にはしない。

現行の `BackdropCandidate.Spec` と Region ごとの共有 surface は、固定の表示仕様を共有先へ渡す入口である。
画面固有の UI を持つために、ゲーム側へ共有 presenter の振分けや追加の表示仕様型を要求してしまう。
`UnitySortingOrderTarget` は `UIDocument` と `Canvas` に限定され、共通 API から他の表示方式へ差し替える境界も不足している。

## 提案

### 調査に基づく設計の境界

[画面管理の比較調査](../../70_research/navigathena_screen_architecture.md)を根拠とし、画面を活動させる管理側と、活動中の処理を実装するゲーム側を分ける。
Caliburn.Micro の Screen の役割、Prism の活動通知と表示アダプター、UnityScreenNavigator の内部で管理する backdrop を参照する。
CommonUI の Active は必ずしも最前面への入力配送を意味しないため、その状態名や条件をそのまま採用しない。
以下は、共通 Runtime が画面管理を実行し、Unity と各拡張アダプターが具体操作を担当する一つの再設計案である。
公開署名の抜粋と利用コードは未実装の契約案であり、現行 API の説明ではない。
利用方法、活動と所有、アダプター境界は本書、演出の段階と完了は[遷移演出の契約](navigathena_navigation_transitions.md)を参照する。
現行実装への参照は個々の操作の実現根拠であり、提案コード全体が現在実行できることを意味しない。

既存の Route、Region、NavigationDefinition、画面 catalog、発行元に紐づく IScreenNavigation の責務は維持する。
Route は行き先のデータ、定義は許可する構成と下層の扱い、catalog は生成方法を保持する。
画面ロジックの起動や資源取得を、Route のコンストラクターや catalog 構築時には行わない。
下層の表示・入力・保持方針と矛盾する、独立した Fullscreen フラグを追加しない。
この文書のフル画面は、下層を隠す定義を持つ画面を指す。

画面実体は、Runtime が一回の生成から終了まで管理する Controller、表示先、取得資源のまとまりであり、Controller 単体や Scene 一つとは同一視しない。
一つの画面を Scene と追加の Prefab で構成しても、画面の初期化・活動・終了を管理する窓口は一つとする。
View の部品が増えたことだけを理由に Route や Region を増やさず、独立した遷移先と履歴を必要とするかで分ける。
この管理単位を利用者が毎回組み立てる公開 Screen 型として追加することは要求しない。

### 配置構成ではなく所有と使用期間を管理する

Navigathena の共通 Runtime は、ゲームの初期シーン、常駐シーン、GameObject の親子構造を所有単位の前提にしない。
ゲーム側が View とサービスをどこに配置し、どの期間保持するかを選ぶ。
Runtime が知るのは、その資源を誰が所有し、どの画面や演出が使用中で、終了してよい条件が成立したかである。

| ゲーム側の構成例 | ゲーム側が選ぶ保持期間 | 共通 Runtime の契約 |
| --- | --- | --- |
| 常駐シーンに管理オブジェクトと共通 View を配置する | そのシーンを所有するゲーム側の期間 | 既存 View を追加取得せず使用し、所有元を勝手に終了しない |
| DontDestroyOnLoad のオブジェクトが View と Host を保持する | そのオブジェクトの実際の生存期間 | 元のシーンが変わっても、同じ実体が生存していれば参照を別の実体へ付け替えない |
| 初期シーンのオーバーレイだけを起動後に終了する | 起動用表示の利用が終わるまで | 所有元の終了要求に対し、使用者の停止と返却を先に完了する |
| 必要な表示を個別に生成・破棄する | 個別の所有者が定める期間 | Scene 単位の寿命を強制せず、その所有者と使用者の関係で終了を調停する |

初期シーンは Root Region や初期 Route とは異なる。
Root Region は履歴と画面構成の論理単位であり、Unity のシーンや常駐ルートを表すものではない。
初期シーンやオーバーレイのためだけの Route を履歴へ追加せず、通常の Reset や Clear で管理対象外のゲームサービスを解放しない。

| 対象 | 所有する単位 | 終了条件 |
| --- | --- | --- |
| 外部が用意した View とサービス | ゲーム側の所有者 | 登録した使用者の停止・返却後に、ゲーム側が具体的に終了する |
| NavigationHost | ゲーム側が選ぶアプリケーションの所有者 | ShutdownAsync の終了成立後に手放す |
| Navigation が取得した画面と資源 | Host 内の画面実体管理 | ロジック、演出、借用先の使用終了後に解放する |
| 画面に接続した演出オブジェクト | Host 内の対応する画面実体 | 入退場ごとの再生終了では破棄せず、画面実体の終了で終了する |
| 遷移用 factory が作った演出オブジェクト | Host 内の遷移操作 | 一回の生成から再生・復旧・後始末が終了するまで |
| 演出の一回の再生 | Host 内の遷移実行 | 借りた実体の再生・整合・停止を確認し、使用を返却するまで |

生成コードの場所、C# の参照保持、資源を破棄する権限は同一ではない。
外部の View を渡して演出を作っても、その View の所有権は移さない。
Host より長く生存する View も、Host より先に使用を終了する View も、所有と使用期間を分ける同じ契約で扱う。
Unity アダプターは実体の同一性、生存と消失、具体的な取得・解放を扱い、共通 Runtime は停止・返却・解放の依存順を扱う。
ネイティブ実体が実際には存続する配置変更だけを、共通側で画面終了や資源消失と判定しない。

構成の自由は、使用中の資源を予告なく破棄しても利用を継続できる保証ではない。
通常の終了は所有者から終了要求を通知し、使用終了を待つ。
管理外の Destroy や強制アンロードなどで実体が先に失われた場合は、アダプターが消失を報告し、Runtime は古い利用者を失効させて障害・復旧として扱う。

### 使用者の手順と API の所属

通常利用者は、行き先を定義し、機能ごとに画面の生成方法を登録し、一つの Host を起動・終了する。
画面には取得済み View とゲームサービスを渡し、活動開始時に、その活動期間に限定された Navigation を受け取る。
画面の操作処理に、ロード、フェード、他画面の停止、ブロッカーの切替、Scene の解放を並べない。

| API またはコード | 提供元 | 利用目的 |
| --- | --- | --- |
| Route、NavigationDefinition | 共通 Navigathena | 行き先のデータ、Region、許可する構成、下層の表示・入力・保持を定義する |
| ScreenCatalog.Build、RegisterScreens、RegisterScreen | 共通 Navigathena | Region と Route の組に画面生成処理を登録する |
| RegisterScreen に渡す async ラムダ | ゲーム側 | View を取得し、ゲームサービスとともに Presenter へ渡す |
| ScreenCreationContext.Resources | 共通 Navigathena | 生成処理の開始前から存在する、管理付き取得の窓口を使う |
| creation.LoadScreenAsync | Unity アダプターの拡張メソッド | Scene を取得し、指定した View の表示構成を接続してから View を返す |
| AddressablesSceneAcquisition | Addressables アダプター | AssetReference に対応する取得・ハンドル解放を実行する |
| IViewAdapter、IScreenAnimator | 共通 Navigathena | 表示・物理入力の反映と、入場・退避・復帰・退出の演出契約 |
| ScreenPresentation | Unity アダプター | 取得済み View の表示構成を Inspector でまとめる |
| ScreenCreationContext.SetTransitionEffect | 共通 Navigathena | 画面実体が保持する全体演出と、その演出用表示先を接続する |
| ResourceLifetime、ResourceReference<T> | 共通 Navigathena | 外部所有の資源と、その所有者が終了を要求する期間を関連付ける |
| Resources.BorrowAsync | 共通 Navigathena | 外部所有の資源を、画面や演出の管理された使用として借りる |
| NavigationTransitionPreparationContext.RegisterExistingViewAdapter | 共通 Navigathena | 借用済みの既存表示を、現在の表示を維持して遷移へ接続する |
| Resources.InstantiatePrefabAsync | Unity アダプターの拡張メソッド | 指定した取得元から表示実体を生成し、元資源との依存ごと管理する |
| IScreenController、ScreenActivityContext | 共通 Navigathena | 初期化、活動開始、活動停止、直接所有する処理の終了を実装する |
| NavigationHostOptions、RegionNavigationOptions | 共通 Navigathena | 共通ブロッカー、結果通知、Region ごとの演出ルールと既定値を設定する |
| NavigationHost.Create | 共通 Navigathena | catalog、共通設定、必要な実行コンテキストから、資源未取得の共通 Host を構築する |
| Start、StartAsync、Client、ShutdownAsync | 共通 Host | 起動、アプリケーション側の遷移、終了を要求する |

RegisterScreens と RegisterScreen は builder のインスタンスメソッドであり、Unity の拡張メソッドではない。
catalogBuilder は全体の登録用 builder、regionScreens は指定 Region の登録用 builder で、いずれも Navigathena が callback に渡す。
同じ Region へ機能ごとに登録でき、登録用の module interface や自動探索は要求しない。
登録時にはロードも Presenter の生成も行わない。
ゲーム側のラムダが Unity を使用しても、共通のカタログ実装に Unity 依存は生じない。

行き先の定義は次の形とする。
この定義部分は現行の共通 API を使用し、画面生成・活動・演出の再設計とは責務を分ける。
Root を Layered にすることで、Inventory の背後に Main を表示したまま残す。

```csharp
public sealed record TitleRoute : Route;
public sealed record MainRoute : Route;
public sealed record InventoryRoute : Route;

public static class GameRegions
{
    public static readonly RegionDefinitionId Root = new("game");
}

var definition = NavigationDefinition.Build(
    GameRegions.Root,
    RegionCompositionMode.Layered,
    root =>
    {
        root.AddRoute<TitleRoute>(route =>
        {
            route.AllowedEntryOperations = RouteEntryOperations.Reset;
            route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
        });
        root.AddRoute<MainRoute>(route =>
        {
            route.AllowedEntryOperations =
                RouteEntryOperations.Replace | RouteEntryOperations.Reset;
            route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
        });
        root.AddRoute<InventoryRoute>(route =>
        {
            route.AllowedEntryOperations = RouteEntryOperations.Push;
            route.LowerPresentationPolicy = LowerPresentationPolicy.BlockInput;
        });
    });
```

Title から Main へ Replace し、Main の活動から Inventory を Push する。
Inventory の Back はその項目を閉じ、保持した Main の活動を再開する。
この利用に子 Region は不要である。
親画面を残しながら子だけの履歴を独立操作するときに初めて、子 Region の定義と登録を追加する。
Scene や View の数で Region の数を決めない。

次は Title 機能が提供する生成登録の契約例である。
TitleView は取得先の Scene に配置したゲーム側の Component で、ゲーム側の ITitleView を実装する。
画面本体の表示と演出は、同じ GameObject の ScreenPresentation に Inspector で設定する。
titleSceneReference は Scene の取得設定であり、ロード前の View 参照ではない。

```csharp
var catalog = ScreenCatalog.Build(definition, catalogBuilder =>
{
    catalogBuilder.RegisterScreens(GameRegions.Root, regionScreens =>
    {
        regionScreens.RegisterScreen(new ScreenDefinition<TitleRoute>(
            async (creation, cancellationToken) =>
            {
                var view = await creation.LoadScreenAsync<TitleView>(
                    new AddressablesSceneAcquisition(titleSceneReference),
                    cancellationToken);

                creation.SetTransitionEffect(
                    creation.Resources.CreateOwned(() => new TitleTransition(view.TransitionView)),
                    view.TransitionViewAdapter);
                return creation.Resources.CreateOwned(() => new TitlePresenter(view, titleState));
            }));
    });

    mainFeature.RegisterScreens(catalogBuilder);
    inventoryFeature.RegisterScreens(catalogBuilder);
});
```

画面本体の演出を使用しない場合は ScreenPresentation の演出参照を省く。
取得と表示構成の接続は[画面の表示構成と演出](../../30_technical/features/navigathena_screen_presentation.md)を正本とする。
画面自身の位置や透明度の入退場と、その画面が提供する全体演出は異なるため、必要のない SetTransitionEffect も省略できる。
TitleTransition と TransitionView はゲーム側の型と参照であり、シーンを取得した後に初めて接続する。
SetTransitionEffect は演出を再生せず、画面実体の所有下へ登録する。
後続の Presenter 生成や初期化が失敗した場合も、登録済み演出を停止・終了してから、その参照先を解放する。
画面本体と演出は別の表示先へ接続し、同じシーンに置かれていても、画面本体の非表示で演出まで隠れない構成にする。
全体演出の選択方法と生成時点は[演出の取得先と所有](navigathena_navigation_transitions.md#演出の取得先と所有)に従う。
TitleView の UI アダプター用プロパティを、Presenter が使用する ITitleView に含める必要はない。
一つの View が増えるたびに Bootstrap のメンバーを追加する構成にせず、この登録と Scene 参照を Title 機能の構築箇所へ置く。

### Bootstrap が所有するもの

起動処理の所在と Host を保持する期間はゲーム側が決める。
同じ管理オブジェクトが両方を担当してもよいが、起動処理だけを終了する構成では Host と必要な外部所有者を継続する所有先へ残す。
Navigathena 専用の常駐シーン、永続 component、DI コンテナーを必須にしない。
機能とアプリケーションの構成コードが catalog と options を用意し、起動処理が初期 Route と起動固有の演出を選ぶ。
通常の画面別演出を Bootstrap に重複して指定しない。

標準の入口は、資源未取得の Host を保持してから初期構成を開く二段階とする。
途中の取得失敗でも所有者が残るため、Host を返す前にすべての非同期取得を済ませる一段階の factory を必須にしない。
次の例では、ゲーム側が最初のシーンに StartupOverlayView を配置し、ゲームの初期化前から画面を覆っている。
GameRoot はゲーム側の構成例であり、この component やシーンの常駐をライブラリの契約にしない。
overlayLifetime は配置方法ではなく、この View の実際の所有期間を表す。
所有者が別の管理オブジェクトなら、そこで作った ResourceReference を起動処理へ渡せる。

```csharp
public sealed class GameRoot : MonoBehaviour
{
    [SerializeField]
    private StartupOverlayView startupOverlay;

    private readonly ResourceLifetime overlayLifetime = new();
    private NavigationHost? navigationHost;

    public async ValueTask<NavigationResult> StartNavigationAsync(
        ScreenCatalog catalog,
        NavigationHostOptions options)
    {
        if (navigationHost is not null)
        {
            throw new InvalidOperationException("Navigation is already owned.");
        }

        var overlay = overlayLifetime.Reference(startupOverlay);
        var executionContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException(
                "A UI execution context is required.");

        navigationHost = NavigationHost.Create(
            catalog, options, executionContext);

        var startupReveal = new NavigationTransition(
            scope: NavigationTransitionScope.Host,
            createEffectAsync: async (preparation, cancellationToken) =>
            {
                var view = await preparation.Resources.BorrowAsync(
                    overlay, cancellationToken);
                preparation.RegisterExistingViewAdapter(view.ViewAdapter);
                return new StartupRevealTransition(view);
            });

        return await navigationHost.StartAsync(
            new TitleRoute(),
            options: new NavigationOptions { Transition = startupReveal });
    }
}
```

Reference と BorrowAsync は View をロード・生成しない。
Reference は所有期間に属する型付き参照を作り、BorrowAsync は使用者を登録してから既存の値を渡す。
StartupRevealTransition のコンストラクターは渡された View を保持するだけで、View の生成、ロード、演出再生を開始しない。
Runtime は factory 開始前から遷移の所有単位を用意し、登録済み表示の使用、演出実体、未終了処理を管理する。

RegisterExistingViewAdapter は、借用済み View に接続した IViewAdapter 実装を、現在の表示を維持して登録する共通 API である。
資源取得、View の複製、所有権移譲は行わず、表示をいったん閉じたり、新しい演出の始端状態を一律適用したりしない。
具体的な描画順や入力制限も、既存の遮蔽を途切れさせない条件でアダプターが接続・検証する。
起動前からの物理入力遮断はゲーム側の配置条件、起動受理後の各制限の調停は Runtime の責務であり、黒い見た目だけを入力遮断の保証にしない。

起動用の要求指定は、すでに表示されている黒幕を解除するための指定である。
タイトルの標準入場は Region の登録方針で選べるが、そのシーンの取得後に利用できる View をロード前の遮蔽に使わない。
起動前からのオーバーレイと、遷移先が提供する演出を同じ「タイトルの演出」として扱わない。
StartAsync の通常成功は、タイトルの準備、必要な演出、黒幕の解除、活動開始、入力開放までを意味する。
ゲーム起動処理は結果を待ち、DestinationCommitted が true かつ PresentationStatus が Ready のとき初期画面を使用できる。
CommittedWithFault の診断を捨てず、OperationCompleted などの観測先でも扱う。
失敗時も Host と終了前の外部所有者を保持し、Bootstrap が StartAsync の後に手動で FadeOut を呼ぶ手順にはしない。

Host の構築はエンジン非依存の NavigationHost.Create を使用する。
次は共通 Host の構築契約の抜粋であり、現行の NavigationDefinition と IPresentationRealizer を受け取る Create とは異なる未実装の署名である。

```csharp
public static NavigationHost Create(
    ScreenCatalog catalog,
    NavigationHostOptions? options = null,
    SynchronizationContext? executionContext = null);
```

この操作は catalog と設定を検証して資源未取得の所有者を返し、Scene、Prefab、黒幕、ブロッカーを取得しない。
UI フレームワーク、常駐方法、初期シーンの構成も選ばない。
画面管理に必要な実体の取得は StartAsync 以降に共通 Runtime の所有下で行う。
アダプターごとに Host の構築、初期化、受付終了、回収待機を実装する入口は設けない。

特定のスレッドを必要とする表示では、ゲーム側の構成コードが適切な実行コンテキストを渡す。
上の例は Unity のメインスレッド上の起動処理から呼ぶことを条件とするが、SynchronizationContext.Current の取得と null 検証自体は .NET の操作である。
この値が非 null であることだけを、Unity のメインスレッドである証明にはしない。
指定された実行先への呼出し開始と Runtime 自身の継続の配送、排他、取消、完了待機は共通側で実装する。
実行コンテキストを省略する場合は特定スレッドへの復帰を保証せず、その制約を必要としない取得・表示実装に限って使用する。
ゲーム側が明示的に別スレッドへ移した処理まで自動的に戻す保証は置かず、固有操作の実行場所に制約があるアダプターは、その操作に必要な条件を検証する。
実行コンテキストへの配送処理を、エンジンごとに同じ構造で作り直さない。
特定エンジンのフレーム API、UniTask、DI コンテナーは共通契約に要求しない。

Regions の設定は catalog と別で、生成方法を変えずに通常の演出を設定できる。
Host 構築時に設定を固定し、外部の Dictionary やルール集合の変更で実行中の方針を書き換えない。
演出を使わない構成では演出設定と接続を省略できる。
フレームワーク名だけを宣言する UseUGUI や、シーン配置済み UI のための共通親 Transform は要求しない。

起動後もオーバーレイを使う場合はその所有期間を継続する。
起動専用で終了する場合は、外部所有者が次の順序で終了する。

```csharp
await overlayLifetime.EndAsync();
// ここからゲーム側が View を破棄し、必要ならその所有シーンを終了する。
```

EndAsync の正常完了は、登録された使用者が停止・返却済みであることを表す。
StartAsync の返却だけから、未終了の借用や任意の外部資源の解放可否を推測しない。
終了した参照からの新しい借用は拒否し、View を継続保持する構成では起動後に EndAsync を呼ぶ必要はない。
この違いだけで、画面管理や演出の実行手順をゲーム側へ再実装させない。

Host 自体の終了では、ゲーム側の所有者が ShutdownAsync を待つ。

```csharp
var shutdown = await navigationHost.ShutdownAsync();

if (shutdown.Completed)
{
    navigationHost = null;
}
else
{
    HandleShutdownFailure(shutdown);
}
```

Host の終了は外部所有のシーンや View を破棄する指示ではない。
ShutdownAsync の待機取消、終了失敗、未終了処理がある状態を、使用中の外部資源を先に解放してよい根拠にしない。

### 通常の利用はシーン内の View と画面ロジックの接続から考える

代表例は、Menu へ遷移するとシーンを読み込み、そのシーンに配置済みの UI を Menu の画面ロジックへ渡す利用とする。
Prefab から新しく UI を生成する場合も扱うが、すべての画面を Prefab として登録することは要求しない。
遷移で新たに読み込むシーンの View を、取得前から Bootstrap が参照する構成にはしない。
初期シーンに既に存在する共通 View の参照は、この制約とは別である。
シーン取得後にそのシーン内の View を型付きの Root として使う能力は、現行の LoadSceneAsync にもある。
見直すのはその能力の有無ではなく、View を受け取る Controller の生成と終了を、共通の所有管理へ接続する利用方法である。

| 利用者が用意するもの | 置く場所と役割 |
| --- | --- |
| Route と下層の扱い | ゲームのナビゲーション定義。行き先のデータと許可する遷移を表し、Scene や View の実体を保持しない |
| Scene／Prefab の参照と画面生成の登録 | その機能を構築する箇所。読み込み方法を選び、取得後の View と必要なゲームサービスから画面ロジックを作る |
| シーン内の UI 参照 | そのシーンの View。ボタンなどへの参照を持ち、表示操作と操作イベントを提供する |
| 画面ロジック | ゲーム側の通常の C# オブジェクト。渡された View とゲーム状態を使い、活動に応じて処理を開始・停止する |
| アプリケーションの起動と終了 | ゲーム側の起動処理と Host の所有者。使用する機能を構成し、起動処理の所在と独立に Host を終了完了まで保持する |

Menu の通常利用は、次の順で成立させる。

1. 利用者は Menu のシーンに UI と View component を配置し、選択した UI アダプターの表示・入力境界を接続する。
2. Menu 機能の構築箇所で、MenuRoute に対するシーン取得と画面ロジックの生成方法を登録する。
3. アプリケーションは機能ごとの登録を catalog にまとめ、Host を保持して起動し、利用者の操作から Route を指定して遷移する。
4. Navigathena は遷移の準備時に生成処理を呼び、取得開始前から資源を所有する。
5. Unity アダプターがシーンを取得し、その取得で得たシーン内の View を型付きで返す。
6. 機能側の生成処理が View を画面ロジックへ渡し、表示先を登録する。
7. Navigathena が画面ロジックの初期化と活動開始を待って表示・操作を成立させ、遷移結果を返す。
8. 覆われた画面の活動停止、戻った画面の再開、履歴から外れた画面の終了を Navigathena が進める。
9. シーンを終了するときは、画面ロジックと、そのシーンを使う他画面の終了後にアンロードする。

シーン自身の画面がその View を使うだけなら、resident 登録、借用先の検索、子 Region は不要である。
別の画面が同じシーン内の UI を使う場合だけ、所有元と利用期間の関連付けを追加する。
Unity の Awake／OnEnable は画面ロジックの活動開始を意味せず、活動中の購読は Navigathena が呼ぶライフサイクルで開始する。
表示アダプターは、準備中の View が公開前に表示・入力参加しないための境界を提供する。

### 登録を機能単位で構成する

画面数に応じて必要になる Route と取得方法の対応は、機能ごとに構築できるものとする。
全画面の Prefab、取得設定、画面生成を、Bootstrap の個別フィールドと一つの巨大な callback へ集めない。
Scene 内の UI が増えただけなら、Scene 内の参照と機能内の接続を変更し、Bootstrap に UI の参照を追加しない。
独立した遷移先が増えた場合は、その機能内で定義と生成登録を追加する。

現行の `UnityScreenCatalogBuilder.RegisterScreens` は、同じ Region に複数の callback から異なる Route を登録できる。
この能力を共通 catalog にも持たせ、登録を分割するためだけの新しい module interface や自動探索は要求しない。
次の RegisterScreens はゲーム側の機能が持つ通常のメソッドであり、ライブラリの新しい実装必須インターフェースではない。

```csharp
var catalog = ScreenCatalog.Build(definition, screens =>
{
    menuFeature.RegisterScreens(screens);
    inventoryFeature.RegisterScreens(screens);
});
```

機能の構築に通常のクラス、設定アセット、既存の DI を使うかはゲーム側が選ぶ。
別のクラスへ全画面の参照一覧を移すだけでなく、関連する Scene／Prefab と生成処理を同じ機能の範囲で管理する。
Route の定義は View の取得前に確定させ、取得したシーンから論理構成を初めて発見することを必須にしない。
定義と生成登録は別の責務として保持しつつ、同じ機能の構築箇所からそれぞれへ登録できるものとする。
定義にない Region・Route と同じ登録範囲での重複は登録時に拒否し、起動時に必要な生成方法の欠落は資源取得前に検証する。
親画面の準備で初めて View と結び付く子画面の生成登録は、対象の親実体と登録世代に限定し、子画面を公開する前に検証する。
後から結び付ける子画面の生成方法まで、Bootstrap が View の実体を使って登録することは要求しない。
後続の遷移でも、対象に有効な生成登録がなければ、その画面の資源取得を始めずに拒否する。

### エンジン固有の処理と共通の管理を分ける

責務の配置は Unity 上で使用するかではなく、その処理の契約や判断が Unity 固有の仕組みに依存するかで決める。
別の表示基盤でも同じ画面管理の判断と手順を必要とする処理は、Unity アダプターに置かない。
共通インターフェースだけを .NET 側へ移し、その実行を Unity 側でしか提供しない構成も、この境界を満たさない。
別エンジンへの変更時に、Host の構築・起動・終了、画面の停止・準備・復旧、資源の使用終了待ちをアダプター側で再実装する必要があれば、責務の配置が誤っている。
アダプターが共通契約に対して実装するのは、取得、解放、表示反映、生存確認など、その環境の固有 API でしか実現できない具体操作である。
Unity 型を持つ協力オブジェクトに依存していることと、その呼出し順を管理する責務が Unity 固有であることを区別する。

| 処理 | エンジン非依存の責務 | Unity または UI アダプター固有の責務 |
| --- | --- | --- |
| Host の構築・起動・終了 | catalog と設定の検証、起動状態、初期要求、受付終了、回収待機、未終了所有を管理する | 固有資源の取得・解放を実行する。Host の状態機械は持たない |
| 実行先への配送 | 指定された実行コンテキストへの呼出し開始・継続、排他、取消、完了待機を実装する | 固有 API の実行場所に制約があれば、その条件を満たす具体操作を行う |
| 画面の活動 | 構成から活動を判断し、開始・停止と非同期完了を管理する | 活動の判断とライフサイクル呼出しは持たない |
| 資源の寿命 | 所有・借用の依存を追跡し、使用終了後に解放を要求する | Scene、Prefab、Component などを具体的に取得・解放する |
| 画面の復旧 | 対象世代を識別し、再構築と再公開を進める | Unity オブジェクトの消失を検出し、資源を再取得する |
| ブロッカー | 使用対象の選択、共通実体の保持、切替、古い操作先の失効を管理する | 具体的な表示要素を配置し、入力を遮断する |
| 演出 | 開始・取消・完了待機と、使用資源の寿命を管理する | Unity／UI 固有の描画 API を使用する部分だけを扱う。演出実装すべてを Unity 固有とはしない |
| 表示順・入力 | 構成上の前後関係と許可状態を決める | 描画方式の制約を検証し、具体的な描画順・入力配送へ反映する |

論理状態を扱う Core と、画面・ブロッカーの実体を扱う共通の実行処理は、責務を分けた上でどちらも .NET 側に置く。
以下で Runtime と呼ぶのは、このエンジン非依存の画面・ブロッカーの実行処理である。
既存の IPresentationRealizer を介した論理状態との境界は維持し、共通の実行処理から具体的なエンジン操作をアダプターへ委譲する。
IPresentationRealizer、IPresentationTransaction、IPreparedPublication は共通の実行処理内の契約とし、通常の表示アダプターを追加するための公開実装要件から外す。
画面の生成登録、準備・停止・終了の順序、復旧、ブロッカーの再使用を、アダプターごとに再実装させない。
同期的な確定区間と実行コンテキストへの配送は、どちらも共通 Runtime が実装する。
確定区間を守る実行処理を、Unity のメインスレッドという利用条件を理由にアダプターへ移さない。
Unity オブジェクトの寿命と画面の活動期間を同一視せず、エンジンからの消失通知を共通の障害・復旧処理へ渡す。

### プロジェクトと配布単位

共通の画面管理は既存の MackySoft.Navigathena プロジェクトへ置き、論理状態と画面実体の管理は内部の責務単位で分ける。
契約だけ、または内部の実行処理だけを配布する必要はないため、Abstractions や Runtime という追加プロジェクトには分割しない。
エンジンと UI フレームワークの任意依存を、次のアセンブリとパッケージの境界で切る。

| プロジェクト・アセンブリ | 配置する責務 | 依存先 |
| --- | --- | --- |
| `MackySoft.Navigathena` | Host の構築・起動・終了、実行コンテキストへの配送、定義・履歴・操作、画面とブロッカーの契約と生成登録、活動・寿命・復旧の実行、IViewAdapter の共通契約 | .NET。Unity と各アダプターを参照しない |
| `MackySoft.Navigathena.Unity` | GameObject・Component・Transform・Scene の取得、取り付け、解放、実体の同一性と消失検出、固有操作の実行条件の検証 | Navigathena と Unity Engine |
| `MackySoft.Navigathena.Unity.UIToolkit` | UIDocument・VisualElement の表示、並び、入力遮断、UI Toolkit 固有の生存・描画制約 | Navigathena、Unity アダプター、UI Toolkit |
| `MackySoft.Navigathena.Unity.UGUI` | Canvas・GraphicRaycaster などによる表示、並び、入力遮断、uGUI 固有の制約 | Navigathena、Unity アダプター、uGUI |
| `MackySoft.Navigathena.Unity.Addressables` | Addressables によるアセットと Scene の取得・解放 | Unity アダプターと Addressables。UI アダプターは参照しない |

GameObject と Scene の処理は Unity アダプター内でディレクトリを分ける。
この二つは同じエンジン依存を持つため、画面管理の一般処理と分離する目的だけで、さらに別の配布単位にはしない。
UI Toolkit と uGUI は独立に選択できる依存であり、Unity アダプター本体からどちらも参照しない。
Unity Editor 用の処理は既存の Editor 専用アセンブリに残し、実行時のプロジェクトへ依存を戻さない。

共通側では、利用者向けの契約と管理側の実装を同じ階層へ混在させない。
画面契約と catalog は Screens、操作ハンドルは Operations、取得前所有の拡張契約は Resources、カスタムブロッカー契約は Blockers、演出契約は Transitions、表示基盤の契約は Presentation、Host の構築は Hosting、終了結果の観測は Lifetimes に置く。
計画、取引、画面所有、ブロッカー選択、終了追跡、資源依存の実装は Runtime 配下で各責務に分け、利用側へ公開しない。
Scene 内の取り付け先や Unity オブジェクトの同一性を、共通側の Region や所有関係の定義にしない。

### 管理する寿命と内部構造

| 管理単位 | 生存期間 | 失効または終了する条件 |
| --- | --- | --- |
| 論理 Entry | 履歴へ追加されてから除去されるまで | Back、Replace、Reset、所有する履歴の終了 |
| 画面実体の世代 | 一回の生成から、その Controller と View の使用終了まで | 保持方針による解放、履歴からの除去、消失と再構築 |
| 活動期間 | 一回の ActivateAsync の開始から活動停止まで | 構成上の活動停止、開始失敗、実体消失、Host 終了 |
| 遷移操作 | 要求の提出から終端結果の確定まで | 成功、拒否、取消、復旧を含む失敗 |
| 資源の取得・借用 | 取得前の登録から、使用停止と終了の確認まで | 実際の使用者がすべて終了し、返却・解放が成立したとき |
| 画面所有の演出実体 | 画面の準備中の登録から、参照中処理の終了と画面実体の終了まで | 再生終了では破棄しない。画面の終了・消失・再構築、Host 終了で終了する |
| 遷移所有の演出実体 | 一回の factory 呼出しから、参照中処理と直接所有資源の終了まで | 成功、取消、失敗、Host 終了 |
| 演出の再生期間 | 既存または生成済み演出の使用開始から整合・停止確認まで | 一回の遷移の成功、取消、失敗。所有元の資源を先に解放しない |
| 外部所有の View | ゲーム側が用意してから所有期間の終了まで | Host 全体の寿命とは独立。登録された使用者の終了後に外部所有者が解放する |

一つの Entry に複数世代の実体が順番に対応し、一つの実体に複数の活動期間が順番に対応する。
操作の結果が返っても、切離し済み資源の終了は残り得る。
この違いを一つの Screen 状態や CancellationToken に畳まない。
内部の所有記録を利用者へ一つずつ生成させる API にはしない。
画面や演出を作った C# メソッドの場所ではなく、登録先と取得先の契約で所有単位を決める。
外部所有の View を使う遷移も、画面所有の演出を使う遷移も、再生期間の取消・排他・整合を共通 Runtime が管理する。
画面が履歴から外れても、演出中の表示や取得資源は使用終了まで保持できるが、画面の活動をそのために再開しない。

共通 Runtime の内部は次の責務に分ける。
これは公開インターフェースの一覧ではなく、同じ保証を複数アダプターへ複製しないための分担である。

| 内部責務 | 所有する判断・状態 | 委譲する処理 |
| --- | --- | --- |
| 要求受付と計画 | 発行元、対象範囲、履歴差分、参加条件、予約 | なし。エンジン操作を呼ばない |
| 操作の実行 | 進行段階、取消受付、確定、復旧先、終端結果 | 画面実体管理と表示・演出の呼出し |
| 画面実体管理 | factory、Controller、世代、活動 context、復元値 | ゲーム側の生成・初期化・活動・終了 |
| 資源所有と終了追跡 | 取得実体、借用関係、終了依存、未終了記録 | アダプターの具体的な取得・解放 |
| 表示構成の管理 | 前後関係、出力許可、各理由による入力制限、登録世代 | 表示アダプターの検証と反映 |
| ブロッカーの管理 | 選択、共通実体の再使用、対象接続、即時切替 | カスタム内容の準備と表示 |
| 演出の管理 | 方針の選択、実体の解決、再生ごとの状態と排他、実行条件、段階、参照中資源 | ゲーム側・表示側の演出実装 |

画面活動、演出、ブロッカーのいずれも履歴を独自に確定しない。
論理確定は一つの管理主体が行い、外部 callback の await はその短い区間の外へ置く。
入力制限は遷移、構成、障害の理由ごとに保持し、ある操作の終了で別の理由を解除しない。
資源の借用や演出中の使用は、操作受付の予約を解放した後も終了確認まで残る。

標準の一つの Host は、一つの合成された画面順と入力境界を持つ。
Region の履歴が独立していることは、表示と入力も自動的に独立するという意味ではない。
そのため、共通ブロッカー一実体を構成上の選択位置へ移して再使用できる。
独立 Region の要求も、共通ブロッカーなどの表示反映は調停する。
同時に独立した複数の遮断面を必要とする構成まで、一つの物理ブロッカーで実現できるとは約束しない。
その追加の合成モデルを、通常利用に必須の公開概念として導入しない。

### 現行 API と内部処理の再配置

移動先はファイル名の Unity の有無ではなく、その型の判断と操作から決める。
例えば現行の PresentationOrderCoordinator は Unity を冠していないが、Unity の描画対象操作を含んでいる。
逆に UnityTerminationCoordinator の終了順と未終了所有者の追跡は、Unity 固有の操作ではない。

| 現行の対象 | 見直し後の責務と配置 |
| --- | --- |
| `UnityScreenCatalog` と builder | .NET 側の `ScreenCatalog` と builder。Route と生成方法の対応・検証を保持する |
| `IUnityScreen` | .NET 側の `IScreenController`。構築済みの画面ロジックの初期化・活動・停止・直接所有するゲーム資源の終了を実装し、画面本体の資源取得や物理表示の一括反映は持たない |
| `UnityScreenContext` | .NET 側の生成処理用 `ScreenPreparationContext`。識別情報・共通の取得所有と表示先登録を渡し、活動用 Navigation は ScreenActivityContext へ分ける。Scene／Transform の操作は Unity アダプターへ分け、Controller の初期化には渡さない |
| `IRestorableUnityScreen<TState>` | .NET 側の `IRestorableScreenController<TState>`。画面固有の復元値を取得し、Scene 取得と分ける |
| `IUnityNavigationChangeHandler` | .NET 側の `INavigationChangeHandler`。確定した構成の変更通知だけを受ける |
| `UnityNavigationHost` と現在の Core 用 `NavigationHost` | 起動・受付終了・完了待機・未終了資源の保持を共通の `NavigationHost` へ統合する。Unity 固有の資源型と表示実装への接続は取得・View アダプターへ分け、Unity 専用 Host は公開しない |
| `UnityPresentationRealizer`／`UnityPresentationTransaction` | 計画を画面実体へ反映する共通の実行処理へ再編する。Unity 操作と消失検出はアダプターへ委譲する |
| `UnityTerminationCoordinator` と終了観測型 | .NET 側で依存順・終了待機・失敗時の所有保持を管理する。具体的な Scene unload や Destroy は実装側が行う |
| `IUnityResourceTransitionOrderResolver` と context | .NET 側の `IResourceTransitionOrderResolver` と `ResourceTransitionOrderContext`。共通の許可条件と資源依存から取得・先行解放の順を選ぶ |
| `UnityScreenResources`／`UnityScreenResourceRegistry` | 画面間の所有・借用・登録世代の追跡は共通側へ分け、Component の解決と Scene／Prefab の取得操作は Unity 側へ残す |
| `PresentationOrderCoordinator`／`UnitySortingOrderTarget` | 共通の前後関係・入力許可・切替の管理と、各 UI アダプターの具体的な描画順・入力反映・検証に分割する |
| `RegionPresentationHost` | Transform を扱う具体的な取り付け先は Unity 側に残す。論理 Region、登録世代、所有・借用期間の管理は共通側へ分ける |
| `PresentationOutputRoot` | GameObject の出力制御と、UIDocument／Canvas の選択・操作を分割する。UI 型の switch を Unity 共通側へ残さない |
| `UnityMainThreadDispatcher` | 現行の Current、Post、スレッド ID、非同期完了の処理は .NET の機能であり、共通の実行補助へ移す。実行先は構成コードから受け取り、固有 API の操作に必要な実行条件だけをアダプターで扱う |

資源の管理を共通化しても、Unity の Scene や Component を汎用 object に包む API は作らない。
共通側が知るのは所有者、利用者、登録世代、終了を待つ必要のある処理と共通の解放契約であり、エンジン資源の構造ではない。
エンジン側は具体的な資源間の依存を登録し、その依存から停止・解放の順を決める処理は共通側が一度だけ実装する。

### world の所有とアダプターの接続

利用側は共通の NavigationHost を一つ所有し、StartAsync、Client、State、Recovery、ShutdownAsync と終了状況の観測を使う。
現行も `UnityNavigationHost` が Core の NavigationHost を内部で所有しており、利用者が二つを個別に管理する契約ではない。
見直すのは所有入口の個数ではなく、Unity 側にある汎用的な起動・画面管理・終了処理の配置である。
初期画面の準備、初期化失敗時の回収、未終了資源を保持したままの終了結果も、共通の world 所有者が扱う。
Host を取得するための Unity 専用 facade は設けず、ゲーム側は共通の NavigationHost.Create を使用する。
エンジン名を変えた Host や Navigation を追加せず、固有の資源操作と View 操作だけを差し替える。

ゲームの構成コードは、使用する機能の登録を共通 catalog にまとめ、Host の共通設定と実行環境への接続を選択する。
ゲーム側の起動処理は、その構成と配置済み View を接続する。
Host はゲーム側が選ぶ所有者が終了まで保持し、起動処理の所在と保持先が同じであることは要求しない。
個々の View の取得方法と必要な取り付け先は機能側で設定し、具体的な UI との接続は Scene／Prefab 側に配置できる。
画面ロジックは Navigation とゲーム側の View 契約を使用し、Scene や Canvas の取得方法を知らなくても実装できる。
表示アダプターの実装者に画面の取引や停止・復旧手順を書かせず、具体的な出力・入力反映、制約検証、消失通知だけを実装させる。
各エンジンの callback を受ける接続処理が必要でも、それを画面の活動開始・停止の判断主体にはしない。

| API の利用者 | 使用・実装する公開契約 | 引き受けない管理処理 |
| --- | --- | --- |
| ゲームの構成コードと Host の所有者 | NavigationDefinition、ScreenCatalog、NavigationHost と選択したアダプターの設定 | 内部の取引・終了追跡・復旧処理の組立て |
| 機能を構築するコード | Route の定義、画面生成の登録、ScreenPreparationContext と標準の取得・表示先登録 | 論理確定、画面の活動条件、共通の終了依存の解決 |
| 画面ロジック | IScreenController、IScreenNavigation。必要なら復元値と構成変更通知 | 他画面の活動判定、ブロッカーの寿命管理 |
| カスタムブロッカー | IBlockerPresenter と必要な場合の IBlockerAnimator | 共通・専用実体の選択、保持、切替の調停 |
| 表示アダプター | IViewAdapter などの表示反映・制約・消失通知の契約 | 画面の生成登録、活動と履歴の状態機械 |
| Unity 資源取得の実装 | Scene・アセットの Unity 固有の取得・解放契約 | 画面間の所有・借用から導く共通の終了順序 |

共通の NavigationHostOptions は observer、共通ブロッカーの生成方法、資源取得順などのエンジン非依存の設定を扱う。
Unity の取り付け先、Addressables のキー、描画方式の基準値はそれぞれのアダプター設定へ置く。
BaseSortingOrder の整数値は共通の履歴順ではなく具体的な描画順への変換設定なので、通常の画面登録や共通 Host の必須引数にしない。
共通の順序は各アダプターで同じ前後関係として実現し、異なる描画基盤を混在させたときに実現できない順序は公開前に拒否する。
表示先の登録に加えて、使用する UI フレームワーク名だけを Host へ宣言する操作は要求しない。
シーン内の View を使用するためだけに、共通の親 Transform を指定する操作も要求しない。
必要な実行コンテキストの指定と、障害時にも維持する入力境界の準備を区別する。
実行先への配送と入力保護の管理は共通側で行い、入力の具体的な遮断は各 UI アダプターで検証する。

Unity を使う場合も同じ共通 Host が、次の効果と所有契約を一続きに提供する。

| 段階 | 共通 Host が保証すること | 利用側が行うこと |
| --- | --- | --- |
| Host の構築 | catalog と設定を検証し、Scene・Prefab・入力保護用の実体を取得せずに共通 Host を返す | 使用する機能とアダプターを選び、返された Host を保持する |
| StartAsync | 構築時に指定した実行先で、必要な入力保護と初期画面を共通の所有管理下で準備する | 初期 Route を指定し、結果と診断を扱う |
| 起動失敗・資源消失 | 取得途中と終了失敗の所有者を保持し、共通の回収・復旧処理を通して具体的な再取得をアダプターへ要求する | Host と借用元を保持し、未確定の失敗と確定後の障害を区別する |
| ShutdownAsync | 受付を止め、受理した処理と所有資源の終了を管理し、全終了した場合だけ正常完了する | await の正常完了後に Host を手放す。終了例外時は未終了の所有を保持する。外部所有の資源は、その所有者が EndAsync で全使用者の終了を待ってから解放する |

この起動・終了処理はアダプターではなく共通 Host の実装とする。
具体的な入力配送・描画設定は UI アダプター、Scene／Prefab の選択は機能側の設定へ分ける。
共通設定、固有資源の取得設定、UI 固有の設定を、一つの Unity 用 Host 設定へ混在させない。
共通 Host の構築から終了までを同じ実装で通し、取得・表示の具体操作だけを交換できることを実証条件とする。

### ライフサイクルの通知先とロジックの所有者

通常の登録対象は、画面実体一つのロジックの初期化、活動、停止、終了を引き受ける `IScreenController` とする。
これは画面の役割を表す契約であり、View component の基底型でも、すべてのゲームロジックを直接実装させる型でもない。
ゲーム側の実装が画面ロジックを直接持っても、既存 Presenter へ委譲してもよい。
ライフサイクルを呼ぶためだけの新しい管理主体として `IScreenLifecycle` を追加しない。
インターフェースの実装先からゲーム側の設計パターンを決めず、実際にどの処理と資源を所有するかを明示する。

| 主体 | 担う責務 | 所有しないもの |
| --- | --- | --- |
| Navigathena Core | 履歴、構成、論理的な参加条件、発行元の有効性を判断する | View の操作、ゲーム側の処理の開始・停止の実装 |
| Navigathena のエンジン非依存の Runtime | 画面とブロッカーの実体を所有し、準備、活動変更、復帰、終了の順序と非同期完了を管理する | ゲームの業務ルール、具体的な Unity API の操作 |
| 機能側の画面生成処理 | 使用する Scene／Prefab や既存 UI を選び、管理された取得を待ち、表示先と子画面の関連付けを登録して Controller を作る | 画面の活動開始、取得失敗した所有者の独自追跡、終了順の実装 |
| ゲーム側の IScreenController 実装 | 渡された View とゲームサービスを使い、自分の画面の初期化・活動・停止・終了を実装する。必要なら Presenter へ委譲する | 画面本体の取得・登録、他画面の活動判定、借用サービス全体の寿命、管理付きで取得した View の二重解放、ブロッカーの生成・表示・終了の調停 |
| Presenter／ViewModel | 表示状態の構築、画面操作とゲームの処理の接続など、ゲーム側で割り当てられた表示ロジック | 画面の非活動化を理由とする共有ゲーム状態の破棄 |
| ゲームのアプリケーション・ドメイン | ゲーム状態、ルール、進行、画面をまたぐ処理や編集セッションを、その用途の寿命で管理する | View の構成や UI 部品の有効状態 |
| View | 表示、演出、具体的な UI の操作イベントを提供する | ゲーム進行の正本、画面活動の判定 |
| Unity アダプター | Unity 資源の具体的な取得・解放、生存監視、固有操作に必要な実行条件の検証を実装する | Host の構築・起動・終了、実行コンテキストへの汎用配送、画面の活動条件、共通の停止・復旧手順、ブロッカーの選択・保持方針 |
| UI アダプター | 指定された出力・入力状態と表示順を具体的な UI へ反映し、生存と描画方式の制約を検証する | 履歴、活動条件、ブロッカーの選択、ゲーム側の購読の意味 |

Runtime が IScreenController の実体と、準備 context を通じて取得した View の取得単位を所有する。
画面実装は自分の処理と直接所有するゲーム側オブジェクトを終了し、Runtime はその完了後に管理対象の View を解放する。
管理付きで取得した同じ資源を画面側からも Dispose する契約にはしない。
画面制御と Presenter の責務を一つのオブジェクトで実装してもよく、別々にする場合だけ必要な処理を委譲する。
Presenter が既存の View を借用する構成では、Presenter へその View の取得・破棄を押し付けない。
Navigathena が画面単位で呼ぶライフサイクルの窓口は一つとし、その配下へどう処理を分けるかはゲーム側が決める。
View と Presenter の双方を自動探索して二重に通知したり、子 component を一括で有効・無効にしたりしない。

IScreenController は Unity アセンブリを参照しない通常の C# オブジェクトが実装でき、MonoBehaviour の継承、UI Toolkit／uGUI の型、DI コンテナーを要求しない。
準備の共通契約と、その準備で使用する Unity 資源の取得実装を分ける。
Unity 固有の資源操作は、機能側の生成処理から Unity アダプターを使用し、共通の準備 context の定義には含めない。
画面本体の Scene／Prefab を取得する方法は生成処理へ渡し、Controller に構築用の context や source を保持させない。
ゲームデータの読み込みや、アイコンなど動的な内容の取得は、画面本体の構築とは別に、用途と終了責任を定めたゲーム側のサービスを使える。
その処理から共通の準備 context を再利用して表示先や子 Region を追加することはできない。
直接生成と DI のどちらでも、同じ生成・所有・活動の契約を使う。

処理の実行主体と、サービスそのものの所有者も区別する。
例えば Menu のタブ操作の購読は Menu の活動期間に属するが、その操作から呼ぶ在庫サービスやゲーム進行は Menu の所有物ではない。
活動停止では Menu の購読を解除して新しい操作を止め、在庫サービス自体は停止・破棄しない。
画面を離れても完了させる保存処理は、その処理を所有するゲーム側の機能に残し、画面への結果反映だけを画面の寿命に従わせる。

### 公開名が表す責務

型名はその対象が何を担うかを表し、メソッド名は呼出しによって何が起こるかを表す。
別ライブラリの名称を、そのライブラリでの責務と異なる対象へそのまま当てはめない。

| 公開名 | 名前で区別する責務 |
| --- | --- |
| `IScreenController` | 一つの画面のロジックと直接所有するゲーム資源を制御する。画面の構築元、見た目だけの実装、複数画面の活動を決める管理側とは分ける |
| `InitializeAsync` | 構築済みの依存を使い、画面ロジックを一度だけ初期化する。Scene／Prefab の取得や活動開始を意味しない |
| `IBlockerPresenter` | ブロッカーのカスタム内容、操作の接続、表示用資源を扱う。下層の入力可否や、自身の選択・保持・終了時期を決めない |
| `IViewAdapter` | 個々の View に対し、共通 Runtime の表示順・表示許可・物理入力許可を具体的な UI 操作へ変換する。ゲーム側の View 契約や画面ロジックの主体ではない |
| `IBlockerAnimator` | ブロッカーの内容の登場・退場を演出する。画面遷移全体や物理的な入力遮断は進めない |
| `BlockerScreenContext` | 現在そのブロッカーを使用する画面の識別情報と操作窓口を渡す。遮断される下層画面や、ブロッカーの所有者を表さない |
| `ScreenPreparationContext` | 画面の生成処理に限って、管理付き取得と表示先・子画面の登録窓口を渡す。初期化や活動には渡さない |
| `ScreenActivityContext` | 一回の活動期間の取消 token と、その期間に限定した Navigation を渡す。画面実体そのものの寿命とは異なる |
| `NavigationOperation` | 一つの提出済み要求の結果待機と取消要求の窓口。実行と資源の所有は Host に残る |
| `BlockerPreparationContext` | カスタムブロッカーの表示資源を準備する窓口を渡す。画面の構築や活動を制御する窓口ではない |

Controller と Presenter はここでの役割を表し、ゲーム側に特定の設計パターンや追加のクラス階層を要求しない。
画面側は活動する処理全体を引き受け、ブロッカー側は構成から選ばれた内容の提示を引き受けるため、両者を同じ Controller 名で一括りにしない。
これらの共通契約は、所属アセンブリ、引数、戻り値、実行処理まで Unity 非依存とし、名前に Unity を付けない。
Unity を名前に含めるのは、Unity 固有の資源や API を実際に扱うアダプターの契約・実装に限る。
Unity 上で実行するという利用条件だけを、共通の管理型や API に Unity を付ける理由にしない。
`IScreenLifecycle` という名称だけでは、通知を受ける契約なのか、サイクルを進める管理主体なのかが決まらないため、本提案のロジック主体の名前には使わない。
IBlockerPresenter はカスタム表示そのものを用意する役割なので、表示資源を準備する PrepareAsync を持つ。
構築済みの View を使う IScreenController の InitializeAsync と、同じ処理としてまとめない。

### 画面のライフサイクルからロジックを開始・停止する

Navigathena が画面制御オブジェクトへ活動開始・停止を要求し、その実装が自分の処理を接続・切断する。
入力購読だけでなく、その活動期間に必要なイベント購読と非同期処理も対象とする。
表示アダプターの無効状態や、ブロッカーの View の有無からロジックの寿命を決めない。

```csharp
public interface IScreenController : IAsyncDisposable
{
    ValueTask InitializeAsync(CancellationToken cancellationToken);
    ValueTask ActivateAsync(ScreenActivityContext activity);
    ValueTask DeactivateAsync();
}

public sealed class ScreenActivityContext
{
    public CancellationToken CancellationToken { get; }
    public IScreenNavigation Navigation { get; }
}
```

ScreenActivityContext の生成と失効は Runtime だけが行い、利用者用のコンストラクターは公開しない。
CancellationToken は活動期間全体の終了を表し、ActivateAsync の呼出し終了を意味しない。
ActivateAsync は開始できたところで完了し、画面を閉じるまで待ち続けない。
開始処理を取り消す場合はその活動期間自体を失効させるため、同じ用途の activation token を別の引数に重ねない。
初期化と遷移操作の取消は、活動期間の token と別である。

| サイクル | ゲーム側の処理 | Runtime の保証 |
| --- | --- | --- |
| InitializeAsync | 取得済み View とサービスから初期表示を作る | 実体ごとに一回。生成処理の後、最初の活動前に呼ぶ |
| ActivateAsync | 操作購読と活動中処理を開始する | 初回と復帰ごとに新しい context を渡し、必要な入場演出の完了後に呼ぶ |
| DeactivateAsync | 購読を解除し、活動中処理の停止完了を待つ | 先に活動窓口を失効させ、活動 token を取り消してから呼ぶ |
| DisposeAsync | 残る処理と直接所有するゲーム側オブジェクトを終了する | Controller の使用終了後に管理付き View を解放する |

同じ Controller の lifecycle を並行に呼ばない。
活動窓口の失効を先に確定し、CancellationToken の callback やゲーム側の停止処理は、論理状態の排他区間の外で実行する。
ActivateAsync が途中で失敗しても context を失効させ、開始済みの処理を DeactivateAsync で回収する。
その停止が終わらなければ、同じ実体を再開したり View を解放したりしない。
InitializeAsync の失敗、取消、未開始でも、取得済み Controller は DisposeAsync の対象とする。
後始末に取消済みの活動 token を渡して即座に打ち切らない。
任意のゲーム処理を強制終了できるとは保証せず、未終了の実体と依存を保持して障害を公開する。

活動用 Navigation は Entry、画面実体の世代、活動期間の三つに固定する。
同じ Presenter を再開しても、前の context と Navigation は失効したままとする。
古い callback が新しい活動の窓口を参照しないよう、購読時に渡された context を捕捉する。
Navigation の失効は新規要求の拒否であり、既に受理された操作の取消ではない。
共有サービス、保存処理、ゲーム全体の進行は、そのゲーム側の所有期間に残す。

### シーンの取得後に画面ロジックを生成する

生成 callback に渡す ScreenPreparationContext は、画面の識別情報、Resources による管理付き取得、表示先と子画面の登録窓口を持つ。
活動期間が存在しない生成段階では、画面活動用の Navigation を渡さない。
画面とブロッカーの生成登録を保持する ScreenCatalog も .NET 側に置く。
NavigationDefinition は使用できる Route、子 Region、下層の扱いを資源取得前に確定し、ScreenCatalog はその定義に対する生成方法を登録する。
親画面の準備時に子画面の生成方法を登録する場合でも、論理構成の定義までその準備完了へ依存させない。
同じ Route 型を異なる Region で使用できるため、生成登録は Region と Route の組に対応させ、全 Region への暗黙の登録にしない。
この context は対象の RegionId を持ち、表示先や子 Region との共通の関連付けは生成 callback の中で登録する。
Transform を含む現行の RegionPresentationHost や、Scene／Prefab を取得する CreateResources は共通 context に移さない。
Prefab の生成などで取り付け先が必要な場合だけ、具体的な取り付け先と Region の対応をアダプター側で扱う。
シーンに配置済みの UI はそのシーンの配置を使い、取得を理由に別の Transform へ取り付け直さない。
この対応は world と登録世代に所属し、画面ロジックから静的な検索先や Unity 型へのキャストを要求しない。
具体的な取り付け先は準備開始時の登録世代へ固定し、非同期取得の途中で新しい世代へ差し替えて使用しない。
子 Region の所有・借用期間は共通側で管理し、その関連付けだけを理由に Unity の親 Transform への取り付けを強制しない。
生成 callback の終了後にその context を保持しても取得・登録操作はできず、Runtime が段階の制約を検証する。
表示先の登録は Scene／View の所有権の移譲ではなく、Runtime が表示順と安全用の境界を制御するための関連付けである。

RegisterScreen には、準備 context と CancellationToken を受けて ValueTask<IScreenController> を返す生成 callback も用意する。
登録自体は同期処理であり、非同期の生成 callback は遷移の準備時にだけ Runtime が呼ぶ。
同期の生成 callback も利用でき、非同期の View 取得を必要としない画面に非同期 factory を強制しない。
二つの登録方法は同じ画面所有と準備の実行処理へ接続し、別のライフサイクルを作らない。

次は Menu 機能側の登録処理の提案であり、現在のライブラリで実行できるコードではない。
MenuView は読み込むシーンに配置するゲーム側の component で、IMenuView を実装する。
MenuPresenter、menuState、menuSceneReference、Regions.Root はゲーム側の型と設定であり、menuSceneReference はシーンを識別する AssetReference であってシーン内の View の実体ではない。
画面本体の表示構成は MenuView と同じ GameObject の ScreenPresentation で設定する。

```csharp
screens.RegisterScreens(Regions.Root, regionScreens =>
{
    regionScreens.RegisterScreen(new ScreenDefinition<MenuRoute>(async (creation, cancellationToken) =>
    {
        var view = await creation.LoadScreenAsync<MenuView>(
            new AddressablesSceneAcquisition(menuSceneReference),
            cancellationToken);

        return creation.Resources.CreateOwned(() => new MenuPresenter(view, menuState));
    }));
});
```

この LoadScreenAsync は ScreenCreationContext に対する Unity アダプターの拡張であり、資源取得の LoadSceneAsync と表示構成の接続をまとめる。
共通の構築 context の定義に Unity 型を追加しない。
その必要性は、現行の `IUnityScreenResources.LoadSceneAsync` と `IUnityScenePreparation.Root` が既に提供している、シーン取得後にそのシーン内の View を使う操作に対応する。
変更点は、画面が個別に CreateResources と DisposeAsync を呼ぶ現行の所有入口を、生成 callback の呼出し前から存在する共通の準備所有へ接続することである。
表示方式の宣言だけを行う設定メソッドとは異なり、この操作にはシーンの取得という効果、取得済み Root という結果、次の所有・失敗契約がある。

| 条件 | LoadSceneAsync と共通 Runtime が保証すること |
| --- | --- |
| 取得開始 | Unity の取得・終了操作を実行する所有単位を共通側へ登録してから取得を開始する |
| 取得成功 | その取得で得たシーンだけから対象 component を解決し、シーンと結び付いた Root を返す。別の読み込み済みシーンを検索しない |
| Root がない、または曖昧 | 成功したように別の View を選ばず、準備失敗として取得したシーンを回収する |
| 取得・生成の取消や例外 | callback が画面を返せなくても取得単位を回収する。取消できないエンジン処理の完了を追跡し、取得と解放を競合させない |
| 画面の終了 | Root を使用する画面ロジックと他画面の終了後にシーンを解放する。利用者に Root の個別 Destroy を要求しない |
| 終了失敗 | 使用中の View や終了失敗した所有者を保持し、終了結果と診断へ公開する |

Scene の準備は、現行の `SceneAcquisitionContext` と同じく Additive で取得し、Scene を有効化してから View を解決する。
対象シーンのすべての root GameObject とその子孫を、非アクティブな component も含めて調べ、指定型が一つだけある場合に成功する。
Scene の有効化は Controller の活動開始ではなく、Awake／OnEnable から活動期間の処理を開始しない。
View の表示と入力は、Scene／Prefab の配置設定と UI アダプターの接続によって公開前から閉じておく。
読み込み後の接続だけで、接続前に表示や入力が漏れなかったことを保証したとは扱わない。
現行の `PresentationOutputRoot.Awake` にも、公開前に出力と入力を閉じる仕組みはある。
共通化ではこの配置側の責任を失わせず、選択したアダプターへ接続したすべての出力・入力経路で、生成・初期化中の非公開状態を検証する。

Addressables は例で選んだ取得方法であり、共通の画面登録と画面ロジックの必須依存にしない。
標準のシーン取得を使うために、画面ごとの IViewSource の実装、専用の View factory interface、追加の source 変数を要求しない。
Scene／Prefab のどちらを使うかを知らない画面ロジックへ、取得設定を注入する必要もない。
取得用の拡張メソッドを配置する Unity アダプターと、AddressablesSceneAcquisition を提供する Addressables アダプターの依存は分ける。

ScreenActivityContext.Navigation は、その画面実体と活動期間へ固定された IScreenNavigation を返し、任意の画面の操作権限を生成しない。
Runtime は callback の呼出し前に準備の所有範囲を作り、callback が途中で失敗しても、取得済みの View と失敗した取得単位を回収対象に残す。
callback が返した Controller を所有してから生成段階を閉じ、その Controller の InitializeAsync を呼ぶ。
callback は活動中の購読や継続処理を開始せず、それらは画面の ActivateAsync で開始する。
画面本体の資源取得は管理された取得窓口を使い、コンストラクターから未登録のロードや継続処理を開始しない。
コンストラクターが独自に取得してから例外で失った未登録の資源まで、Runtime が発見・回収できるとは保証しない。
callback は開始した取得を await してから返し、未完了の取得や登録を初期化・活動段階へ持ち越さない。
callback の失敗・取消でも取得と登録の窓口を閉じ、進行中の取得を Runtime の所有下で収束させる。
Controller には取得済みの View、Route の引数、必要なサービスを生成時に渡し、準備 context そのものを渡さない。
画面操作用の Navigation は ActivateAsync の活動 context から受け取る。
Presenter は借用した View を破棄せず、Runtime は Presenter の終了完了後に View を解放する。
DI を使う場合も、同じ Presenter や View をコンテナーと Runtime の両方が終了する所有契約にはしない。

### 画面ロジックには取得済みの View を渡す

TitlePresenter 自身が IScreenController を実装する。
以下の ITitleView、ITitleState、MainRoute はゲーム側の型である。
ITitleView は SetTitle と StartRequested イベントを提供し、取得・破棄・表示アダプターの API は持たない。

```csharp
public sealed class TitlePresenter : IScreenController
{
    private readonly ITitleView view;
    private readonly ITitleState state;
    private Action? startRequested;

    public TitlePresenter(ITitleView titleView, ITitleState titleState)
    {
        view = titleView;
        state = titleState;
    }

    public ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        view.SetTitle(state.Title);
        return default;
    }

    public ValueTask ActivateAsync(ScreenActivityContext activity)
    {
        activity.CancellationToken.ThrowIfCancellationRequested();
        startRequested = () =>
        {
            activity.Navigation.Replace(new MainRoute());
        };
        view.StartRequested += startRequested;
        return default;
    }

    public ValueTask DeactivateAsync()
    {
        if (startRequested != null)
        {
            view.StartRequested -= startRequested;
            startRequested = null;
        }
        return default;
    }

    public ValueTask DisposeAsync() => default;
}
```

この例はイベント購読だけを所有するため、停止は同期的に完了する。
遷移結果は Host の OperationCompleted と診断へ配送され、ボタン処理が待機者である必要はない。
一時停止後に古い startRequested が呼ばれても、そのラムダが捕捉した活動窓口は失効済みで、新しい活動の窓口へ付け替わらない。
購読解除は画面側、遷移要求の有効性検証は Runtime が担い、一方を他方の代わりにしない。

活動中に非同期処理を動かす画面は、返された Task を追跡し、activity.CancellationToken を渡す。
DeactivateAsync はその Task の停止を待ち、活動終了による取消と実際の障害を区別する。
ゲーム側が開始した処理を Runtime が自動探索する仕組みにはしない。
View の更新も活動中だけに限定する場合は、await 後に活動の有効性を確認し、対象の実行 context 上で反映する。
活動外でも必要な表示更新は実体の寿命へ所属させ、DisposeAsync と復元値の捕捉に対して整合させる。
画面をまたぐ処理はゲーム側のフローが所有し、終了した View へ結果を反映しない。

### 取得方法と所有者の違いを通常利用から分ける

| View の用意の仕方 | 機能側が指定すること | Navigathena の管理と終了 |
| --- | --- | --- |
| 遷移先シーンの配置済み UI | シーンの取得方法と、そこから取得する View の型 | シーンを取得して View を返す。画面の停止・終了後にシーンを解放する |
| Prefab から生成する UI | Prefab の取得方法と必要な配置先 | 生成した実体を所有し、利用終了後に破棄する。元アセットはその所有・借用関係に従って解放する |
| 親画面などが所有するシーン内の UI を別画面で使う | 所有元と対象 UI の識別 | 同じ View の同時利用を制限し、画面終了で借用を返す。借用中の View を含むシーンを先に解放しない |
| 起動前から存在する共通 UI | 配置済み View の参照と実際の所有期間 | 表示を再取得せず使用を登録する。遷移ごとに破棄せず、外部所有者の終了要求では使用者を先に止める |
| 別の終了期間を持つ外部シーンの UI | 既存 View と、その所有者が保証する利用期間 | View とシーンを勝手に破棄せず、接続・購読と借用を終了する。所有者による通常のシーン終了は利用終了を待つ |

Scene 内の UI が複数あるだけで、それぞれを別 Route にする必要はない。
一つの画面の構成要素なら、その画面の View が参照する。
別の履歴項目として遷移する UI だけ、画面登録と必要な所有・借用関係を持たせる。
現行の RegisterResident／BorrowResident は親画面などの所有シーンから借りる場合の根拠であり、ゲーム側が所有する任意の既存シーンへそのまま適用できる入口とは扱わない。
借用した同じ実体を保持中に、同じ実体を必要とする別の画面を重ねる場合は、暗黙の共有や複製をせず、利用者が識別できる準備失敗にする。
シーンの再取得時は、その取得で得た新しい View と生成処理を使い、前の実体への参照を再使用しない。
外部所有の接続では、ゲーム側が実際の所有期間を登録する。
長期間保持する表示にも短期間の表示にも同じ契約を使い、Host の終了まで外部所有者が必ず存続するとは仮定しない。
単に Destroy を省略するだけでは制御中処理の停止と生存監視は成立しない。
借用中の表示・入力・配置を誰が制御するかと、返却時に復元する状態の範囲を接続時に定め、所有者と借用画面が同じ状態を同時に書き換えない。
返却時は画面ロジックの終了と表示先の切離しを待ち、借用で変更した制御状態を返却契約に従って戻してから所有者へ制御を返す。
返却完了を保証できなければ借用を未終了として残し、予期しない消失は正常な返却ではなく障害・復旧として扱う。

### 外部所有の資源を型付きで借りる

ResourceLifetime は、外部所有者が資源の使用終了を要求するための共通の期間である。
Scene、GameObject、Host の型ではなく、実際に一緒に終了する資源のまとまりへ対応させる。
同じ Scene にあるだけで全資源を一つの期間へ束ねず、移動後も実体が存続する場合に元 Scene の終了をその実体の終了と同一視しない。
ゲーム側のシーン管理やオブジェクト管理への接続は、その所有者またはアダプターが行う。

```csharp
// 共通 Navigathena の公開契約案。実装は省略する。
public sealed class ResourceLifetime
{
    public ResourceReference<T> Reference<T>(T resource) where T : class;
    public ValueTask EndAsync();
}

// ResourcePreparationContext の操作
public ValueTask<T> BorrowAsync<T>(
    ResourceReference<T> resource,
    CancellationToken cancellationToken = default) where T : class;
```

Reference は存在する値を、所有期間と資源の同一性へ固定した型付き参照として登録する。
ResourceReference はロード元の設定でも、任意のサービス検索キーでもなく、通常利用者へ破棄権を渡すハンドルでもない。
返す値を後から別世代の実体へ差し替えず、終了中・終了済みの期間からの新しい Reference と BorrowAsync は拒否する。
View ごとの Source クラスは要求せず、所有者が持つ期間から必要な参照を作る。

BorrowAsync は、呼出し元の画面または演出の所有単位を使用者として登録してから値を返す。
利用側が返却用 IDisposable を保持する必要はなく、Runtime がその利用者の使用終了後に返却する。
複数の利用者が同じ資源へ依存できるが、同じ表示を同時に書き換える権限は別途排他的に取得する。
別の参照や別の IViewAdapter 実装で同じ表示を包むことで排他を回避できないように、接続先の実体同一性でも照合する。
部分的な factory 失敗でも借用は Runtime の管理下に残り、未終了の処理が参照している間は返却しない。

EndAsync は所有者からの終了要求であり、新しい使用を閉じ、登録された使用者の停止・返却を Runtime へ要求する。
実行中の遷移には既存の取消・確定・復旧契約を適用し、活動中の画面には活動停止と資源の使用終了を適用する。
論理確定を巻き戻す権限や、代わりの Route を選ぶ権限は外部所有者へ渡さない。
表示が成立しなくなる場合は該当範囲を利用不能として通知し、無関係な画面まで一律に終了しない。
所有者の終了要求は、停止対象自身の callback から自分の終了を待つ形で呼ばず、外部の所有側から行う。

EndAsync の正常完了は全使用者の返却完了を意味し、外部資源そのものを破棄しない。
外部所有者がその完了後に、具体的な Destroy、アンロード、サービス終了を行う。
使用終了が失敗した場合は例外と未終了記録を残し、所有期間は新しい使用を閉じたまま終了未成立となる。
同時の終了要求は同じ終了処理を待ち、終了に失敗した利用者を黙って再実行しない。
終了しない callback があれば完了を偽らず、所有元を解放可能と報告しない。

この期間の終了要求と、実体が先に失われたという消失通知は異なる。
Unity アダプターは後者を具体的な View と取得・接続世代へ対応付ける。
参照の存在だけでネイティブ実体を保持できるとは扱わず、非同期終了を待てない破棄後通知だけを正常な返却手順にしない。
具体的な型付き参照の内部同一性、所有者終了と遷移確定の競合、生存監視は、受入検証で成立を確認する。

### View の取得開始前から終了までを管理する

画面、ブロッカー、演出が新たに資源を取得する場合は、共通の ResourcePreparationContext による取得前所有を使う。
既に存在する View は、追加取得を行わず外部所有の表示として借用・接続する。
通常利用者は LoadSceneAsync などの標準拡張を呼び、独自の取得方式を追加する実装者だけが次の共通契約を実装する。

```csharp
public interface IResourceAcquisition<T> : IAsyncDisposable
{
    ValueTask<T> AcquireAsync(
        ResourceAcquisitionContext context,
        CancellationToken cancellationToken);
}

// ResourcePreparationContext の公開操作の抜粋
public ValueTask<T> AcquireAsync<T>(
    IResourceAcquisition<T> acquisition,
    CancellationToken cancellationToken);
```

取得実装のコンストラクターは設定だけを受け取り、取得・購読を開始しない。
Runtime は取得実体を所有登録してから AcquireAsync を呼ぶ。
返却する T は利用対象であり、ゲーム側へ解放権を渡すものではない。
取得前、部分取得、成功、取消のいずれの状態でも、取得実体は DisposeAsync で自分の取得処理を停止・回収できるようにする。
取得と DisposeAsync を同時に呼ばず、取消できないエンジン処理もその完了を追跡する。
結果を返す前に取得が失敗し、その解放も失敗した場合でも、Runtime が所有実体を保持して診断へ公開する。
成功した所有ハンドルだけを後から登録する方式にはしない。

ResourceAcquisitionContext は所有単位と世代に固定された消失報告の窓口を持ち、任意のサービス検索や Native View の探索 API は持たない。
消失した取得実体の報告で、後から生成した実体を失効させない。
具体的な Scene unload、Destroy、Addressables のハンドル解放は取得実装が行い、その時点と依存順、未終了実体の保持は共通 Runtime が管理する。

ScreenPreparationContext.Resources、BlockerPreparationContext.Resources、演出準備 context の Resources は同じ契約を使う。
窓口は各 callback の有効期間で閉じるが、取得実体の所有は callback 終了後も残る。
callback は開始した取得を await してから返す。
利用中の Controller、演出、子画面を終了してから資源を解放し、単純な取得の逆順だけで依存を代用しない。
Controller の終了が失敗して使用終了を保証できない場合は、先に View を解放しない。

Scene／Prefab の標準利用のために、View ごとの Source クラスやメンバー一覧を要求しない。
取得設定を再使用する場合は、ゲーム側の生成ラムダや取得設定が新しい取得実体を作る。
共通側が知るのは所有と利用期間であり、Scene や Component を object に包んで共通側から取り出す構造にはしない。
既存の`取得開始前の終了登録`は、この所有境界の実現材料である。

### 遷移を要求する API と結果の待機

画面内の要求は ScreenActivityContext.Navigation を使う。
画面をまたぐアプリケーションのフローは、Bootstrap から必要な範囲へ渡した Host.Client を使い、操作先 Region を明示する。
通常の画面には Host 全体の管理権を渡さない。
画面活動の窓口が失効しても、受理済みの操作と取得資源は Host が所有し続ける。

```csharp
public interface IScreenNavigation
{
    NavigationOperation Push<TRoute>(
        TRoute route, NavigationOptions? options = null) where TRoute : Route;

    NavigationOperation Replace<TRoute>(
        TRoute route, NavigationOptions? options = null) where TRoute : Route;

    NavigationOperation Reset<TRoute>(
        TRoute route, NavigationOptions? options = null) where TRoute : Route;

    NavigationOperation Back(NavigationOptions? options = null);

    IScreenNavigation GetRegionNavigation(RegionTarget target);
}

public sealed class NavigationOperation
{
    public Task<NavigationResult> WaitAsync(
        CancellationToken waitCancellationToken = default);

    public bool TryRequestCancellation();
}
```

宣言は公開面の抜粋であり、クラスの実装は省略する。
NavigationOperation は Runtime だけが作る、結果観測と取消要求の窓口である。
要求提出時に発行元の検証と受付予約を行い、画面や演出の callback を呼出し元のイベント処理へ同期再入させない。
実行は共通 Runtime の実行 context へ配送し、操作窓口を返してから開始する。
これは競合した要求を後で実行する待機列ではなく、競合要求はその場で拒否する。
継承や Dispose は要求せず、資源の所有者にもならない。
Push は新しい Entry を追加し、Replace は対象履歴の現在項目を置換し、Reset は対象履歴を置換する。
Back は保持中の実体の再開または保存値からの再生成によって前の構成へ戻る。
戻る操作の名前を変更するためだけに Pop を追加しない。
子の初期構成を必要とする場合は NavigationDestinationTree を渡す同じ操作の overload を使う。
GetRegionNavigation は発行元の所有経路内の明示的な対象を選び、同じ活動期間への制限を保つ。
履歴がない場合の暗黙の親 Back は行わない。

| 操作 | 契約 |
| --- | --- |
| Push などの呼出し | 要求を提出する。競合や失効による拒否も、結果を持つ操作として返す |
| WaitAsync | その呼出しによる結果待ちだけを取り消せる。元の遷移は継続する |
| TryRequestCancellation | Runtime が許可する取消境界より前で、操作自体の取消を要求する |
| PushAsync などの簡便 API | 対応する操作と WaitAsync をまとめる。token の名前は waitCancellationToken とする |
| Start／StartAsync | 同じ要求・待機契約で初期構成を開く |
| ShutdownAsync | 受付終了と所有資源の終了を要求し、結果を待つ。待機取消で終了要求を撤回しない |

TryRequestCancellation の true は取消要求の受理であり、停止・復旧・回収の完了ではない。
受理判断と論理確定または先行解放の不可逆境界への移行は、Runtime が同じ操作状態のもとで競合なく決める。
受理済み取消の後に復旧失敗が起きた場合は、通常の Cancelled として成功した取消に見せない。
同じ操作に複数の待機者を許し、一人の待機取消が他の待機や結果を変更しない。
確定後の要求取消は受理せず、Host による安全な完了処理を継続する。

画面活動中に結果を待つ場合は、活動 token を待機側へ渡す。

```csharp
var operation = activity.Navigation.Push(new InventoryRoute());
try
{
    NavigationResult result =
        await operation.WaitAsync(activity.CancellationToken);

    activity.CancellationToken.ThrowIfCancellationRequested();
    ShowResultForThisActivity(result);
}
catch (OperationCanceledException)
    when (activity.CancellationToken.IsCancellationRequested)
{
    // 活動の結果待ちを終える。遷移自体は Host が管理する。
}
```

ShowResultForThisActivity はゲーム側の処理であり、画面を再開させる操作ではない。
Runtime が先に活動 token を取り消すため、待機を含む活動タスクも DeactivateAsync で回収でき、停止待ちと遷移完了待ちが循環しない。
生成・初期化・活動開始・活動停止・演出 callback 自体から、その callback の完了を必要とする Navigation を await することは許さない。
開始中の活動窓口からの要求も活動成立前として拒否し、初期遷移は Bootstrap のフローで要求する。

画面の停止中は新規の画面活動要求を拒否する。
同じ Controller の復帰時には新しい窓口を渡し、過去の窓口は復活させない。
これは、現行の実体世代だけに結び付く Navigation からの設計変更である。
停止中にも必要な保存後の遷移などは、画面活動とは独立したゲーム側のフローと Host.Client の責務へ置く。

### 結果、取消、終了を利用側から扱う

NavigationResult.Kind と DestinationCommitted は、受理、取消、失敗、確定を区別する。
加えて NavigationResult.PresentationStatus を設け、対象範囲の表示・活動・入力方針の成立を区別する。
その型 NavigationPresentationStatus の値は NotEvaluated、Ready、RecoveryRequired とする。
NotEvaluated は拒否などで表示の成立確認を行わなかった結果であり、現在の画面が壊れているという意味ではない。
Ready は要求された画面が必ず開いたという意味ではなく、取り消して元画面へ復帰した場合も含む。
他の独立 Region を含む Host 全体の正常性は、この一つの操作結果で断定しない。

| 結果 | DestinationCommitted | PresentationStatus | 利用側の扱い |
| --- | --- | --- | --- |
| 拒否・競合 | false | NotEvaluated | 受理されていない理由を扱う |
| 準備失敗・取消から旧構成へ復帰 | false | Ready | 必要なら新しい要求を出す。回収中の依存とは競合制御する |
| 通常成功 | true | Ready | 対象構成が使用可能 |
| 演出障害から確定先の表示へ復旧 | true | Ready | CommittedWithFault と診断を扱い、同じ遷移を再発行しない |
| 確定前または後に復旧できない | 実際の確定有無 | RecoveryRequired | 入力保護と所有を残し、Recovery の結果を扱う |

Restoration は旧構成の復元が必要だったか、成立したかを引き続き表す。
PresentationStatus は、復元後の活動・入力を含む使用可能性や、確定先の演出障害からの復旧を表すため、同じ意味ではない。
解放済み View を持つ履歴からの復元失敗と、未確定の単純な拒否を同じ状態にしない。

Host は待機者の有無にかかわらず操作を終端させ、通常の結果は OperationCompleted へ一回通知する。
実行失敗は元の原因と確定状態を持つ NavigationException、成立した取消は OperationCanceledException とする。呼出し側は操作を待ち、UI イベントなどの最上位境界で例外を扱う。
結果通知が例外を投げても確定した結果や資源所有は失わず、通知の障害として扱う。
返却済み結果は変更せず、切離し済み資源の遅延した終了失敗は Host の終了観測へ対象と所有者を記録する。

通常の遷移完了は必要な有限演出と活動・入力の成立まで待つが、安全に切り離した旧資源の終了すべてまでは待たない。
終了中の資源は Host が保有し、同じ設備や借用元を使用する後続要求とは引き続き競合制御する。
操作の受付予約の解除を、未終了の設備を再使用できる根拠にしない。
未終了の処理が現在の表示へ書き込める場合は安全に切り離せておらず、Ready として入力を開かない。

ShutdownAsync は終了が収束して外部依存へアクセスしなくなった場合だけ正常完了する。
終了失敗は元の原因を保持した例外とし、通常の返り値へ読み替えない。
終了失敗した Dispose を無条件に再実行せず、実行中の終了処理と依存資源を保持する。
同じ Host の終了観測と明示的な復旧操作から状況を扱えるようにする。

### 保持、表示、活動を同じ状態にしない

画面実体を保持していること、画面が見えていること、その画面のロジックが活動していることを分ける。
ポップアップに操作を遮られた下層画面は、表示と実体を保持したまま活動を停止できる。
フル画面に覆われた場合は表示も停止するが、保持方針に応じて実体を残せる。
復帰では保持した同じ実体の活動を再開し、初期化から繰り返さない。

| 状況 | 下層画面の扱い |
| --- | --- |
| 入力を遮る非フル画面が重なる | 表示を維持して活動停止。ブロッカーは別に表示する |
| 下層の操作を許す非フル画面が重なる | 下層が引き続き参加できるなら活動を維持する |
| フル画面が重なる | 活動と表示を停止する。実体の保持・解放は保持方針で決める |
| 重なっていた画面を閉じる | 再び参加する画面の活動を開始する |
| 無関係な Region の変更や表示順だけの更新 | 活動状態が変わらなければ開始・停止を繰り返さない |
| 活動中の画面を終了・再構築する | 活動を停止してから旧実体を終了する |

活動を止める対象は、その画面が活動している間だけ必要なロジックである。
見えている下層画面の表示値を更新し続ける購読などは、活動中だけの購読とは分けて保持できる。
ゲーム全体の時計や進行、受理済みの Navigation を、画面の活動停止に伴って一律に取り消さない。
非同期処理をどの寿命へ所属させるかは画面側の責務とし、旧活動期間の遅延処理が新しい期間の View を更新しないようにする。

活動状態は、表示可能性と構成上の入力参加条件から Runtime が判断する。
表示順を安全に更新するための一時的な入力遮断は、それ自体では新しい活動期間を作らない。
親子所有は資源と履歴の関係であり、親の活動停止をすべての子へ機械的に伝播しない。
子のポップアップが活動し、表示された親画面のロジックは停止する構成を許す。

### 保持からの復帰と実体の再生成を分ける

履歴に残る Entry と、それを実現する Controller・View の世代は別の寿命を持つ。
戻る操作を、常に同じ Controller の再開とも、常に新しい Controller の生成とも定義しない。

| 元の画面の状態 | 戻るときの処理 | 初期化と復元値 |
| --- | --- | --- |
| 活動を止めて実体を保持している | 同じ Controller・View の活動を再開する | InitializeAsync は繰り返さない |
| 履歴を残して実体を解放した | 同じ Entry に対して新しい実体を生成する | 新しい実体を初期化し、必要な画面だけ保存した復元値を使う |
| 実体が予期せず消失した | 障害を扱い、Recovery で新しい世代を構築する | 消失した View から最新状態を読めるとは保証せず、利用可能な復元値を使う |

スクロール位置や選択項目などを再生成後に戻す必要がある画面だけ、IRestorableScreenController<TState> と RegisterRestorableScreen を使う。
復元値は画面固有の不変な値とし、共有の在庫やゲーム進行を複製して巻き戻す用途にはしない。
生成処理は復元値を取得済み View とともに Controller へ渡し、InitializeAsync で初期表示へ適用できるものとする。
実体を解放する際は、活動停止後かつ資源解放前に必要な値を確保し、捕捉できない場合は復元可能として扱わない。
画面実装は、保持期間の表示更新と捕捉が競合して途中の状態を保存しないようにし、一貫した不変値を返す。

### 非同期のライフサイクルと同期確定の順序

通常の IncomingFirst の順序は、[遷移演出の標準の実行順序](navigathena_navigation_transitions.md#標準の実行順序)に統一する。
活動から離れる画面を停止した後、選択済みの演出が利用する資源の取得元に応じて、演出の接続と開始時点を決める。
独立した factory または遷移元の画面が提供する演出は、新画面の準備前に BeginAsync を呼べる。
遷移先の画面が提供する演出は、その画面と必要なブロッカーの準備および画面の初期化後に接続し、BeginAsync を呼ぶ。
いずれも必要な PrepareSwitchAsync の完了後に論理確定し、切替後演出、活動開始、入力開放へ進む。
接続を後から行う場合も、演出の選択方針を準備の途中で選び直さない。
演出が未登録なら該当する呼出しを省き、準備と活動の前後関係は変えない。
生成処理の完了後に InitializeAsync を待ち、生成・初期化中には活動期間の処理を開始しない。
解放する実体の復元値は、活動停止後かつ最初の資源解放前に捕捉する。
Core が許可する先行解放を選ぶ場合も、停止と必要な退場演出の完了前に資源を解放せず、既存の復元契約を保つ。
安全に切り離した旧画面の終了とブロッカーの装飾は、通常の遷移結果とは別に所有と完了を追跡する。

同時に変更する対象は、開始では必要な所有元から、停止・終了では依存する側から処理する。
変更のない画面まで活動開始・停止を繰り返さない。
同じ画面のライフサイクルを並行に呼ばず、開始途中に終了が必要になった場合も取消後に進行中の呼出しを回収する。
ライフサイクル内で、同じ Host が受理した遷移要求の完了を待たない。
ユーザー操作の処理から通常の遷移を await することとは区別する。
操作処理が遷移結果を待つ場合は WaitAsync に活動 token を渡し、活動終了によって待機だけが中断できるものとする。
その待機を含む活動処理を DeactivateAsync が回収しても、Host が所有する遷移そのものの完了までは待たない。
遷移要求の所有は Host に移り、その結果を後で View へ反映する場合だけ、発行時の活動期間がまだ有効か確認する。
これにより、自分の停止を待つ遷移と、その遷移を待つ停止処理の循環待機を作らない。

確定前に中断した場合は、既に停止した旧画面を新しい活動期間で再開し、元の表示・入力の成立を確認してから復帰する。
停止途中で失敗した画面は停止済みとせず、復帰不能なら入力を閉じて障害として扱う。
確定後の開始失敗は、論理履歴を巻き戻さず、部分的に始まった処理の停止を試みて表示障害として報告する。
ActivateAsync が途中で失敗した場合も DeactivateAsync を呼ぶため、画面実装は部分的に開始した購読と処理を回収できるようにする。
失敗・取消・未開始の準備でも DisposeAsync の対象にし、所有権を失わせない。
活動の停止に失敗した場合、Runtime は発行元や物理入力を制限できるが、任意のゲーム側コードが停止したと偽って扱わない。
使用中の資源は解放せず、未完了の所有を残す。

画面演出の前後と活動の開始・停止は対応付けるが、同じ意味にはしない。
表示を残して活動だけを止める場合もあるため、活動停止のたびに退場アニメーションを実行しない。
画面演出を使用する場合は、演出の開始・終了も表示側の契約で管理し、画面ロジックの開始時に必要な View が準備・配置済みであることを保証する。
活動開始を、すべての装飾アニメーションの完了と同義にしない。
ブロッカーの生成・表示切替・演出・終了は、画面のロジックとは別に Navigathena が管理する。
遷移全体の演出と画面の入退場を使用する場合の順序、正常完了、取消は、[遷移演出の追加提案](navigathena_navigation_transitions.md)で具体化する。
有限の遷移演出は活動開始前に待つ対象とし、ここで待機条件から除くブロッカーの装飾や画面内の継続的なアニメーションとは区別する。

### 利用者は共通ブロッカーを一つ設定する

ゲームは Navigation world の構築時に、共通ブロッカーの生成方法を一つ設定する。
Navigathena は生成した共通実体を保持し、遮断位置と操作対象を更新して使い回す。
画面ごとに設定したり、遷移のたびに新しい共通実体を作ったりする必要はない。
画面本体の準備、活動開始・停止、終了にブロッカーの管理コードを書かない。
以下は設定 API の提案であり、現在のライブラリで実行できるコードではない。
各ブロッカー、状態サービス、アセット供給はゲーム側の実装である。

```csharp
options.DefaultBlockerFactory = () => new DefaultBlockerPresenter(blockerAssets);
```

ブロッカーが必要な画面にカスタム指定がなければ、この共通ブロッカーを使用する。
画面定義にブロッカーの登録を繰り返す必要はない。

### 必要な画面だけカスタム指定する

非フル画面の Inventory で専用の操作説明やレイアウトを使う場合だけ、その画面のブロッカーを指定する。

```csharp
screens.RegisterBlocker<InventoryRoute>(
    route => new InventoryBlockerPresenter(route, inventoryState, inventoryBlockerAssets));
```

選択の優先順位は、対象画面のカスタム指定、world の共通設定の順とする。
カスタムの必要がない画面は共通設定のままで使える。
カスタム指定は使用する実装を選ぶための設定であり、画面実体へ寿命管理を委譲する入口ではない。

factory は、資源取得や購読を開始していない新しいカスタム実装を返す。
Navigathena は返された実体を所有してから準備を呼び出し、部分的な準備失敗も終了する。
ゲームがライフサイクルの各処理を実装し、Navigathena が呼出順、取消、完了待機、終了責任を持つ。
通常の画面実装に `Blocker` プロパティを追加する必要はない。

操作説明は、カスタムブロッカーが注入されたゲーム状態を読み、必要な変更通知を購読して更新する。
画面はそのゲーム状態を更新し、ブロッカーの生存や表示中かどうかを調べてから View を操作する必要はない。
購読はブロッカー自身の準備で開始し、Navigathena から呼ばれる終了処理で停止する。
画面固有の内容であることを、画面実体の参照や寿命への依存に置き換えない。

ブロッカーの準備で、独自の View に接続した IViewAdapter 実装を登録する。
その出力と入力、表示順を決めて反映するのは Runtime とアダプターであり、IBlockerPresenter に同じ物理制御の窓口を重複して持たせない。
IBlockerPresenter は、カスタム内容の準備、現在の操作対象との接続、所有資源の終了を実装する。
管理付きで取得する View は BlockerPreparationContext.Resources.AcquireAsync を使用し、Presenter 自身の終了と進行中の演出の完了後に Runtime が解放する。

```csharp
public interface IBlockerPresenter : IAsyncDisposable
{
    ValueTask PrepareAsync(
        BlockerPreparationContext preparation,
        CancellationToken cancellationToken);

    void SetScreenContext(BlockerScreenContext? context);
}
```

BlockerScreenContext は現在ブロッカーを使用する画面の Entry の識別情報と、その画面に限定した Navigation を提供する。
遮断される下層画面の集合ではなく、例えば Inventory の背面に操作説明を置く場合の Inventory を指す。
SetScreenContext はその画面に対応する説明や操作へ接続を更新し、null では画面固有の購読・操作先を切り離す。
Runtime は旧対象の操作権限を失効させてから切り替える。
BlockerScreenContext の Navigation は対象と接続世代に固定し、Presenter はイベント接続時にその context を捕捉する。
新しい対象の Navigation へ旧窓口を付け替えず、古い callback が共通実体の新しい対象を操作することを防ぐ。
入力を開くまでその接続からの新規操作を受け付けず、画面活動用 context とも別の接続寿命として管理する。
画面の物理実体が再生成された場合は、必要な接続を更新するが、それだけでブロッカー実体を破棄しない。
表示仕様の型、レイアウト、素材、内部のボタン構成はゲーム側に残す。
画面のライフサイクルをブロッカーが呼んだり、ブロッカーの表示状態から画面の活動を決めたりしない。

### 共通の見た目も画面固有の見た目も同じ入口を使う

| 利用場面 | ゲーム側が提供するもの | Navigathena が行うこと |
| --- | --- | --- |
| ブロッカーを使う画面で共通の暗幕を使う | world の共通設定を一つ | 同じ実体を必要な位置で使い回す |
| Inventory だけ操作説明を表示する | Inventory のカスタム指定と必要なゲーム状態 | 共通実体に代えてカスタム実体を使用する |
| 別画面でも別のレイアウトを使う | その画面だけカスタム指定する | 画面構成に対応する実装を選ぶ |
| 説明文、アイコン、装飾を組み合わせる | 自由な内部構成を持つカスタム実装 | 同じブロッカーとしてサイクルを管理する |
| 非フル画面の背面を透明なまま遮断する | 透明な共通ブロッカーを一つ設定する | 必要な画面で同じ実体を使い回す |

共通実体の所有者は一つの Navigation world とし、利用する画面数だけ所有権を増やさない。
共通から共通への切替では、再生成や終了を行わず、必要な遮断位置と現在の操作対象を更新する。
カスタム実体へ切り替える間も共通実体は保持し、共通設定を使う画面に戻ったら再使用する。
非表示になったことだけを共通実体の破棄理由にしない。
world の終了、またはその実体の復旧が必要な物理消失で、Navigathena が共通実体を終了する。
使い回す際に、前の操作対象の説明、購読、callback を次の対象へ混入させない。
ブロッカーの保持と解放は、必要な入力遮断、切替中の使用、実際の資源依存から Navigathena が判断する。
画面の Dispose をブロッカー終了の通知や前提にしない。
再取得が必要な場合も、Navigathena が登録済み factory から新しい実体を得る。
健全なブロッカーまで画面実体の再生成と一緒に破棄する規則は設けない。

### 画面の入力可否とブロッカーの表示を別に導出する

画面の論理的な入力可否は、画面の積み重ね、被覆、入力方針、遷移と障害の状態から Navigathena が導出する。
ブロッカーを表示しないフル画面でも、下層画面の入力受付を停止する。
ブロッカーの View が非表示になったり失われたりしたことを、下層画面の入力再開条件にしない。

表示するブロッカーは、前面から画面構成をたどって選ぶ。
下層の入力を遮る非フル画面の Route に対応するブロッカーを選び、そこにカスタム指定がなければ共通実体を使う。
ブロッカーを必要としない非フル画面は、それだけでは下層のブロッカーを消さない。
先にフル画面の Route に到達した場合は、それより下の画面のブロッカーを表示しない。
フル画面かどうかは Route の被覆方針によって判断し、View の寸法や UI 部品の有効状態から推測しない。

ブロッカー自体を独立した Route として Push／Back する API は設けない。
Push、Back、Replace、Reset に伴う遮断位置、表示内容、保持、終了の更新を Navigathena が一つのサイクルとして調停する。
現在の操作対象に対応しない説明や callback を、別の対象へ引き継がない。
戻った位置で保持中の適合する実体を使うか再取得するかも、画面の手動操作ではなく Navigathena が扱う。
画面実体一つにつきブロッカー実体を一つ所有することや、画面と同時に破棄することは、この対応関係から導かない。

例えば、フル画面の上へ、下層入力を遮る非フル画面を積む場合、次の対応になる。

| 操作 | 使用するブロッカー |
| --- | --- |
| フル画面を表示する | ブロッカーは表示しない |
| 共通設定の非フル画面 A を Push する | 共通実体を表示する |
| 共通設定の非フル画面 B を Push する | 同じ共通実体の遮断位置を更新する |
| カスタム指定の非フル画面 C を Push する | C の準備後、カスタム実体へ即時に切り替え、共通実体は保持する |
| C の上へ別のフル画面を Push する | 下層のブロッカーを表示対象から外し、下層画面の入力停止は維持する |
| 上のフル画面を閉じて C に戻る | C のブロッカーを再び表示する |
| Back で C から B に戻る | 保持していた共通実体へ即時に切り替える |
| 非フル画面をすべて閉じ、最初のフル画面に戻る | ブロッカーを退場させ、共通実体は保持する |

背景クリックで閉じるか、独自のボタンを置くかはゲーム側が決める。
イベントの操作先と有効性は、Navigathena が現在ブロッカーを使用する画面に対応付ける。
終了済み画面の参照をカスタムブロッカーへ保持させない。
ブロッカーが切り替わった後の古い callback は、新しい画面を操作できない。

### ブロッカー同士は即時に切り替える

表示状態の同期反映と、登場・退場の演出を別の操作として扱う。
次の表の A と B は準備済みの異なる実体を表し、「なし」は表示するブロッカーがないことを表す。
「なし」であっても、覆われた画面の入力受付を再開するとは限らない。

| 変更前 → 変更後 | Navigathena が行うこと | 演出 |
| --- | --- | --- |
| なし → なし | ブロッカーを表示しない | 開始しない |
| なし → A | A の配置と入力遮断を成立させ、表示する | 登場演出を開始できる |
| A → A | 同じ実体の配置と操作対象を更新する | 入退場を繰り返さない |
| A → B | A を非表示、B を表示へ同期的に反映する | 切替演出を行わない |
| A → なし | A の物理的な遮断と操作先を解除し、A を退場させる。各画面の入力可否は別に反映する | 退場演出を開始できる |

「即時」は、準備済み実体の表示切替に、次のフレームや演出完了を待つ中断点を設けないことを意味する。
アセットの読み込みや新しい実体の準備まで同期処理にするという意味ではない。
新しい実体が必要な場合だけ、Navigathena が確定前に factory を呼び、非表示・操作不可の状態で準備する。
保持中の健全な共通実体へ戻る場合は、生成と準備を繰り返さない。

A → B では、切替に必要な入力を閉じた区間で、表示順、表示状態、入力遮断、操作先を一体として更新する。
新旧を同時に表示するクロスフェードや、旧ブロッカーの退場後に新ブロッカーの登場を待つ手順を設けない。
切替途中の入力漏れや、新しい画面に古い説明が付いた状態を外部へ公開しない。
確定前の準備失敗は既存の画面遷移の回収・復元契約に従い、確定後の反映失敗では論理状態を巻き戻さず、入力を閉じて表示障害として扱う。

### 登場・退場の演出も Navigathena が管理する

演出を持つ IBlockerPresenter 実装は、任意の IBlockerAnimator を実装する。
アニメーションなしの実装には追加の契約を要求しない。

```csharp
public interface IBlockerAnimator
{
    ValueTask PlayEnterAsync(CancellationToken cancellationToken);

    ValueTask PlayExitAsync(CancellationToken cancellationToken);

    void SetAppearanceImmediately(bool shown);
}
```

PlayEnterAsync／PlayExitAsync は、ブロッカーがない状態との間で内容の見た目を演出する入口である。
SetAppearanceImmediately は進行中の演出による変更を同期的に失効させ、内容の見た目を指定された終端状態にする。
shown が true なら登場後、false なら退場後の見た目とし、Root の表示許可を意味しない。
例えば不透明度や位置を確定するが、Runtime が持つ物理入力の可否や表示順を変更しない。
Root の表示許可は Runtime が IViewAdapter を通じて反映し、演出の途中値とは別に保持する。
これは非同期メソッドに即時指定を渡して完了を待つ契約ではない。
Navigathena 固有のアニメーション設定表への変換や、画面からの演出開始・取消・完了待機を要求しない。

Navigathena は演出を開始し、取消と完了、失敗、使用中の資源を追跡する。
ブロッカー装飾の完了だけを通常の NavigationResult の返却条件へ追加しない。
画面の有限の入退場と遷移全体の演出は、別の契約として活動開始前に待つ。
入力遮断の成立や解除を、不透明度や演出の完了に依存させない。
退場中の見た目を残す場合も、解除済みの入力遮断や古い操作先を残さない。
フル画面に覆われたブロッカーを、退場演出のためにそのフル画面より前面へ移さない。

登場・退場中に別のブロッカーが選ばれた場合は、その演出を失効させ、新しい構成の表示状態を同期的に反映する。
退場中の A から B を表示する場合も、A の見た目を同期的に取り除いてから B を表示し、新旧の演出を重ねない。
同じ実体が退場中に再使用される場合は、旧退場演出を失効させてから表示し直す。
取消通知だけで即時切替が完了したとは扱わない。
切替では SetAppearanceImmediately を呼び、対象実体の進行中の演出を停止し、以後の古い演出処理が新しい表示状態を書き換えないことを契約に含める。
この同期操作は非同期の後始末まで完了したという通知ではなく、後始末の完了は引き続き Runtime が追跡する。
A → A の通常の配置更新では、進行中の登場演出を最初から再生し直さない。

演出の CancellationToken と有効性は Navigathena が所有し、遷移要求の取消や呼出し元が結果を待たなくなったことによって確定済み画面の安全性を変えない。
物理消失や終了時にも演出を失効させ、資源に触れる処理が完了するまでその資源を解放しない。
非表示の共通実体は保持し、終了が必要な実体の後始末も Navigathena が行う。
演出の失敗で返却済み NavigationResult を変更せず、現在の表示・入力に影響する障害と、安全に切り離した旧資源の終了失敗を区別して通知する。

### Unity と UI Toolkit の依存をそれぞれアダプターに閉じ込める

ブロッカーの登録と Runtime は、.NET 側にあるエンジン非依存の `IBlockerPresenter` と表示契約に依存する。
UI Toolkit を使う `InventoryBlockerPresenter` の内部で、その View に接続した UI Toolkit 側の IViewAdapter 実装を使う。
別の UI フレームワークを選ぶ場合も、カスタム実装の登録と Navigation の操作は変わらない。

IViewAdapter は、個々の View を共通 Runtime の表示・物理入力制御へ接続する契約である。
描画面、画面の取り付け先、UI フレームワーク全体の設定、画面ロジックの主体を表すものではない。
Runtime が指定する表示順・表示許可・物理入力許可を具体的な UI 操作へ変換し、接続先の同一性・生存・描画方式の制約を提供する。
契約は共通側に置き、Canvas や UIDocument を実際に操作する実装は各 UI アダプターに置く。
同じ契約を実装するために、画面の状態機械や所有・復旧の管理を各アダプターへ複製しない。
画面、ブロッカー、遷移演出のいずれの View にも使え、IScreenController の実装を要求しない。

ブロッカーと遷移全体の演出の準備 context は、次の操作で IViewAdapter 実装を登録する。

| 登録操作 | 登録時の扱い |
| --- | --- |
| RegisterViewAdapter | 新たに準備した View の通常表示と入力を、公開まで閉じた状態で接続する |
| RegisterExistingViewAdapter | 借用済みの既存 View の表示を維持して接続する。起動時に既に表示されている黒幕を消さない |

登録は View の生成、親 Transform の変更、所有権移譲を意味しない。
ViewAdapter、TransitionViewAdapter などのプロパティは、ゲーム側の View が IViewAdapter 実装への参照を提供する利用例であり、ゲーム側の ITitleView などへ一律に追加する契約ではない。
ゲーム側の View 契約は文字列やボタンなど画面固有の操作、IViewAdapter は Runtime からの表示・入力指示の反映、Controller は活動処理の開始・停止を担当する。
Runtime が登録と借用期間を管理し、IViewAdapter 自身は履歴、活動、ブロッカーの選択、登録の寿命を判断しない。
表示順、出力許可、物理入力の許可を反映する一つの契約とし、同じ要素を IScreenController と IBlockerPresenter の双方から物理制御しない。
ここでの gate は Runtime が安全のために閉じる物理的な表示・入力の境界であり、ゲーム側が設定する通常の表示状態や不透明度とは分ける。
その境界を閉じるために UI 部品の無効用スタイルを適用することを要求しない。
アダプターによる物理的な入力遮断を、画面のライフサイクルやゲーム固有のロジックの停止の代わりにしない。
入力境界が遮断する対象は、そのアダプターへ接続した入力経路である。
ポインターの遮断だけで、ナビゲーション操作、submit／cancel、ゲーム固有の入力購読まで停止したとは扱わない。
UI アダプターは対応する入力経路を明示して検証し、独自の入力購読は画面の活動に応じて接続・切断する。
共通側に `UIDocument`／`Canvas` の判定や、その二種類しか生成できない sealed な target を残さない。
画面本体の構築は個別の登録操作を使わず、取得 API が設定済みの ScreenPresentation を一単位で接続する。
表示出力だけでなく物理入力の境界も登録するため、現行の `RegisterUiOutput` から名称を変える。
ブロッカーだけを抽象化して本文から依存が漏れる状態にしない。

アダプターは名前空間だけでなく、アセンブリと依存パッケージを分離する。
Unity のアセンブリがなくても、共通の画面管理をビルドして実行できることを条件とする。
各 UI アダプターを使用しなくても、共通層と Unity アダプターをビルドできることも条件とする。
共通契約に object 型の Unity 資源や任意のサービス検索を隠して、実行時に Unity を要求する構成にしない。
生成・解放の具体的な操作と、その順序・所有の判断を分け、Unity の API を呼ばない管理処理までアダプターへ残さない。
各アダプターは、自分が整合した表示順と入力遮断を保証できる範囲を検証する。
異なる描画基盤を同じ数値で無条件に混在できるという保証は作らない。

### ゲーム側に安全用の別ブロッカーを組み立てさせない

画面の独自ブロッカーが失われても、履歴上の入力境界まで消えるわけではない。
Navigathena は遷移中と障害時に画面の入力受付を停止させ、表示アダプターは物理的な入力境界を保護する。
ブロッカーの View の生存を、これらの入力停止処理の前提にしない。
ゲーム側は各 Region に Backdrop と InputBlocker の二種類の surface を供給する必要をなくし、カスタムブロッカーの登録として扱う。
透明な安全用の遮断要素とゲームの表示要素を内部で分けるかどうかはアダプターの責務とする。

## 判断材料

### 旧版と公式資料から確認した設計

外部資料の観測点、実装へのリンク、比較先ごとの制約は[比較調査](../../70_research/navigathena_screen_architecture.md)にまとめる。
この設計は、通知の受け手をゲーム側に置き、活動と実体保持を分け、非同期完了とブロッカーの再使用を管理側で扱うという比較結果に基づく。
CommonUI の入力配送だけで下層ロジックが停止するとは扱わず、Flutter の表示を持つ Route をデータだけの Navigathena.Route へ対応付けない。

画面の入場後に操作を購読し、退場前に解除する使い方を共通契約として扱う。
UI 部品を無効用の見た目へ変える操作と、ゲーム側の処理を接続・切断する操作を区別する。
Unity 側の入口からエンジンに依存しないライフサイクル実装へ通知する責務分離は、特定の DI コンテナーを使う場合だけでなく成立させる。

単に `Interactive` を別の名前の bool に変えることや、UI の有効状態を一括操作する補助だけでは、この利用契約を満たさない。
ブロッカーの共通実体の再使用、画面ごとのカスタム指定、ブロッカー間の即時切替は、本ライブラリに求められた要件であり、比較先すべての共通仕様とは主張しない。

### 現行の UnityNavigationHost の責務監査

現行の UnityNavigationHost は、UnityScreenCatalog、UnityPresentationRealizer、RegionPresentationHost などの型に依存するため、実装上は Unity 依存である。
その依存を根拠に、起動・終了や資源所有の管理責務まで Unity 固有とは判定しない。
実装を責務ごとに分けると、次の配置になる。

| 現行の処理と根拠 | 責務の判定 | 再設計での所属 |
| --- | --- | --- |
| `コンストラクター`で設定を保持し、取得開始前から所有者を用意する | エンジンを問わない所有入口 | 共通 NavigationHost |
| `StartAsync`で重複起動・終了中・未終了資源を検証する | 起動状態と受付条件の管理であり、Unity 固有の判断ではない | 共通 NavigationHost |
| `StartCoreAsync`で取消を接続し、初期化失敗を回収して初期 Reset を要求する | 初期化と遷移の順序、失敗回収、確定後の扱いは共通 | 共通 Host と実行処理 |
| `InitializeAsync`で設備を取得し、表示実装と Core を構築・接続する | 取得の管理と状態の接続は共通。具体的な設備型への結合は別の責務 | 共通 Runtime が取得契約と IViewAdapter を使用し、Unity 型の操作は実装側へ分ける |
| `ShutdownAsync と ReleaseOwnedAsync`で受付を止め、進行中の起動と資源終了を待つ | 依存順、取消、終了失敗の保持は共通 | 共通 Host と終了追跡 |
| `UnityMainThreadDispatcher`で Current、Post、スレッド ID、完了 Task を扱う | 実際の処理は .NET の実行コンテキストへの配送 | 共通の実行補助。実行先はゲーム側の構成から受け取る |
| `RegionPresentationHost`が Transform を保持し、UnityEngine.Object の生存を検証する | 参照対象と具体的な生存判定が Unity 固有 | Unity の取得・配置・生存確認の実装 |
| `UnityNavigationHostOptions`が取得順の方針、設備供給、BaseSortingOrder をまとめる | 共通の資源方針、具体的な取得、描画設定が混在する | 共通設定、取得アダプター設定、UI アダプター設定へ責務ごとに分ける |

UnityMainThreadDispatcher の非 null 検証は、得たコンテキストが Unity のメインスレッドに属することまでは検証していない。
Unity 固有の接続や検証が実装済みである根拠にはしない。
Unity 型へ依存する実装をそのまま共通層へ移すのではなく、共通の管理処理が固有の取得・表示操作を呼ぶ境界へ分離する。
共通化後にエンジン別の Host や Navigation facade として残す管理責務はなく、共通 NavigationHost へ統合する。
Host の構築を代行するだけの UnityNavigation.CreateHost は公開契約に含めない。

### 画面構築の再設計案との照合

構築 API の設計案は、実装済みの API や外部ライブラリの検証結果とは区別する。
この案から、取得元の選択、取得の実行、終了の保証を別の責務とし、Controller の初期化から構築用 context を除く境界を採る。
Scene／Prefab／既存 UI の違いを生成処理へ閉じ込め、既存 Presenter へ取得済み View を渡すという利用を、初期化の署名と失敗時の所有順まで一貫させる。
既存 UI の借用は破棄しないだけでは足りず、制御権と返却完了の契約を必要とする。

一方、Host 全体を一律に Busy とする案は採らない。
現行の[独立した子 Region の操作テスト](../../../tests/MackySoft.Navigathena.Tests/NavigationConcurrencyContractTests.cs)は、同じ状態から準備した操作が逆順でも双方確定することを検証している。
影響範囲が重なる場合は、[予約の契約テスト](../../../tests/MackySoft.Navigathena.Tests/NavigationReservationContractTests.cs)のとおり、準備開始前に Conflict として拒否する。
独立した履歴の操作を妨げず、共有する実体への反映を調停する責務を、共通 Runtime へ配置する。

入力を遮られても下層の活動を常に続ける方針や、下層ごとに任意の活動方針を追加する案も、画面の活動停止を求める要件から自動的には導けない。
本提案では構成に応じた活動停止を維持し、表示用に残す購読と共有ゲーム処理の寿命を分ける。
生成登録の検証は、起動時に確定できる範囲と親の View 取得後に結び付く範囲を区別する。
遷移の成立と後始末を区別する要求は採るが、すべての終了や装飾演出が完了するまで通常の遷移結果を返さない契約にはしない。

### シーン UI の追加案と現行契約の照合

シーン UI の設計案では、利用側の Host 所有、要求取消、発行元の有効性をコードと照合する。
現行の UnityNavigationHost は資源未取得で構築できる一つの所有入口であり、内部の Core と設備の終了も引き受けている。
この入口を共通化する理由は、利用者の二重管理の解消ではなく、エンジンに依存しない管理処理を Unity 以外でも利用できる配置へ移すことである。

要求の token が確定前の遷移自体を取り消すことは、[呼出し元の取消テスト](../../../tests/MackySoft.Navigathena.Tests/NavigationCallerCancellationContractTests.cs)で確認する。
確定後の取消が行き先を巻き戻さないことは、[確定後の取消テスト](../../../tests/MackySoft.Navigathena.Tests/PresentationCarrierContractTests.cs)で確認する。
現行 API の cancellationToken は操作取消を表す。
本案では操作の TryRequestCancellation と待機の waitCancellationToken へ分け、署名と利用コードも変更する。
旧引数を残したまま意味だけを待機取消へ変えた API にはしない。
[ScreenNavigation](../../../src/MackySoft.Navigathena/Runtime/Navigation/ScreenNavigation.cs) の発行元検証も、実体の世代と操作先の現在項目を検証するものであり、ゲーム側の活動期間の停止を代行するものではない。

Host の構築は共通の NavigationHost.Create とし、catalog、共通設定、実行コンテキストを受け取る。
この署名は未実装であり、現行の共通 Host を構築できることだけで、画面実体の共通管理まで成立したとは扱わない。
UI の選択と native 表示設定は各 View アダプターへ分離し、アダプターには共通 Host の構築を代行する API を要求しない。
Controller の構築用 context を除く提案は本提案と一致し、初期化の名称は責務に合わせた InitializeAsync とする。

### 要求と API の対応

| 要求 | 提案による扱い | 確認する外部動作 |
| --- | --- | --- |
| 共通 Host を再使用する | Host の構築・起動・終了と実行コンテキストへの配送を共通側へ統合し、取得・表示の具体操作を差し替える | Unity を参照しない構成と Unity の構成で、同じ Host と Runtime による起動、遷移、起動失敗の回収、終了失敗の保持が成立する。アダプターに同じ管理手順の実装を要求しない |
| 配置構成に依存しない | 所有期間と使用者を共通側で管理し、ネイティブ実体の配置と生存をアダプターに分ける | 常駐シーン、DontDestroyOnLoad、起動後に終了する表示、個別オブジェクトの所有で同じ登録・演出・終了契約が動く |
| 既存オーバーレイから起動する | 外部所有の View を借り、表示を維持したまま起動演出を作る | 起動前から黒幕が存在し、Host 接続で非表示や再生成にならない。タイトル公開まで遮蔽が続き、成功後の View の保持・終了は所有者が選べる |
| 所有と再生を分ける | 画面所有の演出、遷移所有の演出、外部所有の View の終了条件を分ける | 同じ画面演出を入退場で使用できる。一回の演出終了で共有 View が破棄されず、画面終了では使用中の演出より先にシーンが解放されない |
| 遷移先シーンの UI を使う | 機能側の非同期生成処理でシーンを取得し、その Root を画面ロジックへ渡す | Bootstrap が遷移先シーン内のオブジェクトを事前に持たずに遷移でき、取得したシーンの UI が表示・操作できる。自身の View に resident 登録や子 Region を要求しない |
| 画面数の増加を一つの起動クラスへ集中させない | 同じ catalog に機能ごとの登録をまとめる | 既存機能内への遷移先追加とシーン内 UI の追加に、Bootstrap の View 別フィールド追加が不要。起動用 catalog の Region・Route の組が重複した場合は構築時に拒否する |
| 同じ Scene を複数回取得しても正しい UI を使う | 取得単位ごとの Scene だけを解決対象にし、再取得時に新しい View を渡す | 同型 component を持つ別シーンや、終了した前世代の UI を参照しない。対象シーン内で Root が欠落・重複した場合は回収して失敗を返す |
| View 取得後の生成失敗を回収する | 画面生成前から存在する共通の所有範囲でシーンを管理する | シーン取得後に Presenter のコンストラクターが失敗してもシーンが回収され、回収失敗時には所有者が終了診断へ残る |
| 構築と初期化を分ける | factory が取得済みの依存を渡し、生成段階を閉じてから Controller を初期化する | 同じ Presenter を異なる取得元で使用でき、初期化失敗でも Controller の後始末より先に View を解放しない。保持からの復帰で初期化を繰り返さない |
| 準備中に表示や入力を漏らさない | 取得される Scene／Prefab とアダプターが公開前の閉じた境界を用意する | 読み込みから生成・初期化完了までを遅延させても、表示やポインター・キー・操作イベントへの参加が始まらない |
| シーン内の UI を別画面で利用する | 必要な場合だけ借用と所有元の依存を追跡する | 借用する画面の処理が終了するまで所有元のシーンを解放しない。同じ View の同時借用は拒否する |
| 外部所有のシーン内 UI を使う | シーンの所有権を移さず、利用期間と制御の返却を管理する | 画面を閉じてもシーンや View を破棄しない。返却完了後は残った callback が作用せず、定めた表示・入力・配置状態を所有者が再び制御できる。所有者の終了要求時には使用中のロジックを先に止める |
| Unity 自体をアダプターにする | 共通契約だけでなく、画面・ブロッカーの管理処理を .NET 側へ置く | Unity を参照しない画面と表示アダプターで、同じ Runtime による遷移・停止・復帰・終了と共通ブロッカーの再使用が動く |
| 準備から Unity の依存を漏らさない | 共通 context の定義と Unity アダプター側の取得拡張を分ける | 同じ Presenter を変更せず、機能側の生成処理と表示アダプターの差替えで準備・操作・終了が動く |
| 取得失敗時にも所有者を失わない | 取得単位を登録してから AcquireAsync を開始する | View を返す前に取得が失敗し、その後の解放も失敗しても、終了結果に未終了の所有者が残り、依存資源を先に解放しない |
| 既存 Presenter へ取得方法を押し付けない | 管理された非同期生成 callback から画面を返せる | View をコンストラクターで受け取る Presenter を中継専用の画面型なしで使用でき、生成途中の例外でも取得した View を回収できる |
| 障害処理をアダプターごとに複製しない | 資源消失の検出をアダプター、停止・保持・復旧の管理を共通側が担う | Unity を使わない表示先の消失でも、古い画面の入力が止まり、同じ復旧操作で再生成され、旧 callback が新しい画面を操作しない |
| ロジックの主体へライフサイクルを届ける | ゲーム側の画面制御オブジェクトを画面単位で関連付ける | Unity component や UI 型を持たない実装でも、対象画面の開始・停止・復帰でその画面の処理だけが切り替わる |
| 画面の停止で共有処理を壊さない | 活動中の購読と、借用サービスや画面をまたぐ処理の寿命を分離する | Menu を覆っても共有ゲーム状態や保存処理は存続し、Menu の操作受付は止まる。Menu 終了後に保存処理が完了しても、終了した View へ結果を反映しない |
| 画面のサイクルに応じてロジックを開始・停止する | 活動開始・停止の通知で、購読や更新処理を接続・切断する | 活動停止中は対象イベントや更新通知で画面の処理が動かず、復帰後は通知一回につき一度だけ動く |
| 活動停止で UI の見た目を変えない | ライフサイクルと UI 部品の有効状態の変更を分離する | 非フル画面に遮られた下層で表示内容と通常のスタイルが保たれ、表示用に保持した購読による更新も継続する |
| 一時的な入力遮断を活動停止と混同しない | 構成上の活動変化と物理入力の反映を分ける | 無関係な Region の遷移や表示順の更新で、既存の画面の購読が解除・再登録されない |
| 独立した Region を並行操作する | 履歴の影響範囲で競合を判断し、共有実体への反映を Runtime が調停する | 独立した二つの操作が逆順でも双方確定し、重なる操作は準備前に Conflict となる。同じ Controller の開始・停止や共通ブロッカーの更新が競合しない |
| 活動停止の非同期完了を待つ | 活動 token の取消後に DeactivateAsync を待機する | 停止処理を意図的に遅延させても、その処理が使用する View は解放されず、停止前に新しい活動期間を開始しない |
| 活動の開始・停止を並行にしない | Runtime が実体ごとに呼出しを管理する | 開始中の shutdown や物理消失で、開始処理の完了前に同じ資源の終了が走らない |
| 子の操作で親を覆える | 活動は所有階層ではなく構成から導出する | 親画面の購読は止まり、子のポップアップの操作は機能する |
| 停止と破棄を区別する | 活動停止後も保持方針に従って画面実体を保持する | 下層へ戻る際に初期化を繰り返さず、同じ画面のロジックが再開する |
| 履歴を残して画面実体を解放する | Entry と実体の世代を分け、必要な画面だけ復元値を捕捉する | 戻ると同じ Entry に新しい Controller・View が生成され、選択位置は復元されるが共有ゲーム状態は巻き戻らない |
| 要求取消と活動取消を区別する | 確定前の操作は取り消せるが、活動 token を遷移へ自動では渡さない | 確定前の明示的な取消では行き先を確定せず、必要なら元の構成へ復帰する。確定後の取消で行き先を巻き戻さず、発行元の活動停止だけで自身の遷移を取り消さない |
| 実体の世代と活動期間を区別する | 活動ごとに新しい Navigation を渡し、旧窓口を失効させる | 同じ Controller の再開後も前活動の遅延 callback は拒否される。受理済みの操作は活動終了だけで取り消されない |
| 開始の部分失敗と遷移取消を扱う | Runtime が活動の停止・復帰と画面終了を管理する | 開始失敗や終了後には残った購読が動かず、未確定の遷移で止めた元画面は復帰後に一重の購読で動く |
| フル画面ではブロッカーを表示しない | 被覆状態による表示選択と画面の入力可否を別に反映する | フル画面が上に来ると下層のブロッカーは見えなくなるが下層を操作できず、戻ると対応するブロッカーと画面の入力受付が復帰する |
| 画面固有の操作説明を表示する | 登録したカスタム実装が対応するゲーム状態を表示する | Inventory と別画面で異なる View と説明が表示される |
| 内容をゲーム側で更新する | カスタム実装がゲーム状態の変更を購読する | 説明更新で履歴・Entry・画面実体が作り直されない |
| サイクルを Navigathena が管理する | factory の呼出しから終了まで Runtime が所有する | 画面にブロッカー管理コードがなくても、正常遷移・取消・準備失敗・復旧・shutdown が成立する |
| 画面実体の寿命に依存しない | 必要な入力遮断と実際の資源依存から保持・終了を決める | 画面実体の解放・再生成だけで必要な遮断が外れず、健全なブロッカーを強制終了しない |
| 共通ブロッカーを一つ設定して使い回す | world が共通実体を一つ保持する | 画面 A から B への遷移で生成・終了を繰り返さず、遮断位置と操作対象だけが正しく変わる |
| 必要な画面だけカスタムする | 画面のカスタム指定を共通設定より優先する | 共通→カスタム→共通で、同じ共通実体を再使用し、前の画面の説明や callback が残らない |
| 別のブロッカーへ即時に切り替える | 新旧の表示と入力を同期反映する | 共通→カスタム、カスタム→別カスタム、カスタム→共通で、演出待ち、表示の重なり、入力漏れがない |
| 登場・退場の演出を調整する | なし→あり、あり→なしでゲームの演出を実行する | 演出の遅延だけで NavigationResult を遅らせず、画面構成に従った入力停止・再開が演出完了に依存しない |
| 演出中でも次の構成へ移れる | 古い演出を失効させ、表示状態を同期反映する | 登場中の別実体への切替、退場中の別実体の表示、退場中の同じ実体の再使用で、古い演出の遅延完了が現在の表示や操作先を変更しない |
| ブロッカー演出の再生管理を画面ロジックから分ける | Runtime が取消・完了・資源の保持を管理する | 画面に演出管理コードがなく、非表示の共通ブロッカーが保持され、終了対象の資源は使用中の処理が完了してから解放される |
| 遅延した終了失敗を見失わない | 遷移結果と Host の終了観測を分ける | 返却済みの成功を変更せず、遅延した解放失敗の対象と所有者を観測できる。未終了の所有者が使う資源は残る |
| 履歴から戻る | 保存された画面構成に対応するブロッカーを選ぶ | 新しい画面の説明が残らず、戻った画面の内容が表示される |
| 障害時にも必要な入力遮断を維持する | アダプターが画面実体から独立して遮断を維持する | 現在のブロッカーの View が消失したり演出が失敗したりしても下層へ入力が漏れず、切離し済みの旧演出の失敗は現在の入力状態を変えない |
| 待機と実行を分離する | 同じ操作に複数の WaitAsync を許し、待機 token を実行へ伝播しない | 一人の待機取消後にも別の待機者と Host が同じ終端結果を受け取り、画面停止と遷移完了が循環待機しない |
| アダプターを独立に導入する | Unity 基盤と UI・Addressables のアセンブリおよびパッケージ依存を分ける | UI Toolkit なし、uGUI なし、Addressables なしの各構成で、必要な共通・Unity 部分だけがビルドできる |
| UI Toolkit を必須にしない | 共通公開契約と実装アセンブリを分離する | 共通層の参照・公開シグネチャに UI Toolkit 型がなく、別アダプターだけで利用できる |

これらは提案 API の受入条件であり、現行ライブラリの既存テスト成功では代替しない。
公開契約の採用前に、通常の C# による画面ロジック、既存 Presenter の使用、共通とカスタムのブロッカーで、この受入条件を検証する。
共通の動作検証は Unity を参照しないテスト用の View と IViewAdapter 実装を使い、Host の構築・起動・終了と画面管理の実行処理自体は実物を使う。
適用先を記録する .NET の実行コンテキストでも同じ配送処理が動き、Unity 固有の Host を経由しなくても処理の順序と完了が成立することを確認する。
架空の Runtime を模倣したテストや文書の API 宣言だけのコンパイルを、この受入条件の成立とは扱わない。
Unity 側では具体的な資源・入力・描画との接続を検証し、UI Toolkit と uGUI の二種類だけでエンジン非依存の検証を代替しない。
この検証のために別エンジン向けの製品アダプターまで実装することは要求しない。
共通の公開 API を使用する .NET テストプロジェクトは Unity を参照せず、起動失敗時の回収と終了失敗時の保持も検証する。
Unity の GameObject・Scene を扱うテストは Unity アダプターのテストに置き、UI Toolkit と uGUI の反映テストはそれぞれのアダプターのテストへ分ける。
アセンブリの分割に合わせて Unity の asmdef、共有パッケージ生成・復元、利用プロジェクトへの導入、サンプルの参照先も更新することを実装の完了条件とする。
Unity が生成する csproj の編集だけを、プロジェクト境界の修正完了とは扱わない。
現行の IUnityScreen を IScreenController へ改める際は、IRestorableUnityScreen の状態取得契約、catalog、サンプル、資源終了、復旧処理も責務ごとに共通側と Unity 側へ分ける。
画面固有の不変値を取得する IRestorableUnityScreen の責務には Unity 依存がないため、共通側の IRestorableScreenController とする。
現行の IUnityNavigationChangeHandler も共通の PresentationChangeContext を受ける構成変更通知なので、共通側の INavigationChangeHandler とする。
構成変更通知に活動開始・停止を代行させない。
既存の Core の履歴、親子所有、発行元の失効、確定前後の失敗の区別は維持する。

### 利用例の成立範囲と残る実現確認

責務の分離、活動の開始・停止、取得前の所有登録、結果と終了の扱いは、この提案の公開契約として定める。
共通の取得契約は既存の取得前登録の実装、活動期間の処理は CafeSimulator の利用例と比較調査を実現可能性の判断材料にしている。
活動期間に固定した Navigation、複数の待機者、PresentationStatus は本案で追加する契約であり、現行テストの通過では検証できない。
これらの既存実装は、新しい共通 Runtime と API の動作確認そのものではない。
公開 API の採用前に、同期・非同期の生成登録から InitializeAsync までの利用例を、対応する C# 9／.NET Standard 2.1 の共通契約と Unity アダプターでコンパイルする。
取得契約や型推論だけを抜き出したコンパイル確認では、シーン取得を含む利用例全体や、所有・停止・復旧の動作検証を代替しない。

現在のシーン取得と表示構成の接続は、[UnityScreenCreationExtensions](../../../packages/com.mackysoft.navigathena.unity/Runtime/Resources/UnityScreenCreationExtensions.cs) と[利用例](../../30_technical/features/navigathena_usage.md)を参照する。
複数の View がある Scene から対象を明示的に選び、取得した Scene とともに終了する動作は、[ScreenPresentationUnityTests](../../../tests/Unity/Assets/Tests/Editor/ScreenPresentationUnityTests.cs)で検証する。

| 利用場面 | 利用側の記述と管理側の保証 | 設計への影響がある実現確認 |
| --- | --- | --- |
| 通常の一つの Region | 定義と生成方法を登録し、一つの Host で開始・操作・終了する。子 Region や専用ブロッカーの登録は不要 | 実物の共通 Runtime と Unity を参照しない View source で、開始から終了まで通す |
| シーンの配置済み UI | 機能側でシーンを指定し、取得後に Root を Presenter へ渡す。登録のためだけの Prefab 化は不要 | 提案の LoadSceneAsync 拡張を共通の準備所有へ接続し、成功・取消・Root の欠落と重複・生成例外・終了まで実際のシーンで確認する |
| 機能ごとの登録 | Bootstrap は機能の登録をまとめ、各機能が自分の取得指定と生成方法を持つ | 同じ Region への分割登録、重複登録の拒否、機能内の画面追加で Bootstrap の View 別メンバーが増えないことを確認する |
| 下層を残すポップアップ | Push で下層の活動を止め、Back で同じ画面の活動を再開する。共通ブロッカーは Runtime が再使用する | 表示を残して購読が止まること、同じ共通 View が戻ること、同期切替で入力が漏れないことを確認する |
| 取得または画面生成の部分失敗 | 画面を返せなくても、準備の所有範囲が取得済み・取得途中の資源を保持する | 取得失敗と終了失敗を組み合わせ、所有者の保持と診断が共通側で成立することを確認する |
| View を受け取る Presenter | 非同期生成 callback で取得した View を渡し、構築能力を持たない初期化を呼ぶ。Presenter の終了後に View を解放する | 初期化の取消・部分失敗・終了失敗を含め、View の二重終了と、Presenter の遅延処理による解放後の参照がないことを確認する |
| Unity への接続 | ゲーム側の所有者が共通の NavigationHost.Create で Host を構築し、取得開始前から終了成立まで保持する。機能側が取得方法を選び、起動処理の所在やシーン構成は固定しない | 同じ共通 Host で未取得の構築・初期表示・取得の部分失敗・資源消失と再取得・終了失敗の所有保持を通す。指定実行先への配送と各 View アダプターの入力境界を個別に検証し、Unity 専用の起動・終了管理を追加せずに成立することを確認する |
| 親画面内の子 Region | 所有元と子の生成方法を関連付け、子の終了後に借用元の View を解放する | ResourceAcquisitionContext が渡す登録世代と依存登録の操作を、Scene 内の借用・再構築で確認する必要がある |
| 外部所有の既存表示 | 実際の所有期間へ型付き参照を登録し、借用と表示の接続を行う | RegisterExistingViewAdapter が登録時の描画・入力を途切れさせず、同時使用、終了失敗、予期しない消失を検出できることを確認する。View を複製・破棄しない |
| Host より先に終わる所有元 | 終了要求を受けて利用者を停止・回収してから所有元へ返す | 準備中、確定後の演出中、画面活動中に EndAsync を要求し、使用終了より先に完了しないことを検証する。正常返却と予期しない消失を区別する |

Unity の構築と親子の借用に必要な拡張契約について、現在の例だけでは公開シグネチャ全体の成立を確認したとは扱わない。
特に共通 Host の構築・指定実行先への配送・公開前の入力境界が未確認のままでは、通常利用が起動から終了まで完成したとは扱わない。
IViewAdapter の名前と登録例だけで具体的な表示・入力制御の成立を主張せず、接続先の同一性、登録中の生存、既存表示の維持を実装で確認する。
確認結果が共通の所有・活動管理の再実装をアダプターへ要求する場合は、アダプターへの委譲境界を見直す。
見た目の具体化、ゲーム側の Presenter の内部構成、同期的な購読解除か非同期の停止待ちか、選択する取得方式は実装側の自由度として残す。

### 正本へ反映する範囲

採用時は、共有 BackdropCandidate と固定の UI target を前提にした設計を置き換える。
意味と不変条件、画面ライフサイクル、公開シグネチャ、Bootstrap、設備の所有、画面の利用例を同じ方針へ揃える。
正本で Unity Adapter に置かれている取引処理、所有・借用の追跡、画面間の依存順、復旧手順についても、エンジン非依存の管理と Unity 固有の実行へ責務を分ける。
画面固有の View を再び共有の `Spec` へ押し込める受渡し方式は採らない。

## 回答

## 関連文書

- [Navigathena の設計契約](../../30_technical/features/navigathena.md)
- [Navigathena のクラス設計](../../30_technical/features/navigathena_class_design.md)
- [Navigathena の実務サンプル](../../30_technical/features/navigathena_usage.md)
