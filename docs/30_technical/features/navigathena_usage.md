---
authority: canon
---

# Navigathena の使い方

ゲームは行き先、取得元、View とゲームサービスの組み立て、画面固有の処理を書く。Navigathena は履歴・実体・活動・表示・入力・所有・終了を管理する。DI の有無で画面ロジックの契約は変わらない。

通常の履歴復帰と、回答付き画面の呼び出しは別の API で扱う。Back は元の履歴項目へ戻り、InvokeAsync は一回の呼び出しの回答を待つ。回答を ActivateAsync へ配送する API は設けない。

コンパイル対象の利用例は [CampaignNavigationSetup](../../../tests/Unity/Assets/Samples/Startup/CampaignNavigationSetup.cs) と [CampaignMapInstaller](../../../tests/Unity/Assets/Samples/Campaign/CampaignMapInstaller.cs)。Manual、MicrosoftDI、VContainer の三方式を同じ画面で選べる。これらのゲーム側の型はライブラリの必須基底クラスではない。

## Unity の Inspector 設定と起動

1. 画面の Scene に CampaignMapView と CampaignMapInstaller を配置し、Installer の View へ参照を設定する。
2. View に独立した screen-space root Canvas の CanvasViewAdapter、章名 Text、次の章と Back の Button を設定する。通常画面の presentBeforeNavigation は無効にする。
3. 起動時の Scene に黒幕を配置し、StartupOverlay に CanvasViewAdapter と CanvasGroup を設定する。黒幕の presentBeforeNavigation は有効、CanvasGroup の alpha は 1 にする。
4. CampaignNavigationSetup に Addressables の画面 Scene、既存の StartupOverlay、利用する DependencyMode を設定する。
5. ゲーム自身の初期化後に InitializeAsync の正常完了を待つ。所有者を終了する前に ShutdownAsync を待つ。

この例では最初の Scene が黒幕と構成オブジェクトを持つ。常駐 Scene や DontDestroyOnLoad はライブラリの前提ではない。どの構成でも外部所有者が Host と借用の終了を待てるようにする。

## Route は遷移ごとの引数

Unity でも使える読み取り専用の Route を定義する。

```csharp
public sealed record CampaignMapRoute : Route
{
    public CampaignMapRoute(int chapterId) => ChapterId = chapterId;
    public int ChapterId { get; }
}
```

Scene、View、Presenter、サービスや callback は Route に含めない。Route をコンストラクターへ注入せず、PrepareAsync と ActivateAsync の引数で受け取る。

## 画面ロジックは三方式で共用する

```csharp
public sealed class CampaignMapPresenter : IScreenLifecycleHandler<CampaignMapRoute>
{
    private readonly CampaignMapView view;
    private readonly CampaignService campaign;
    private UnityAction? next;
    private UnityAction? back;

    public CampaignMapPresenter(CampaignMapView view, CampaignService campaign)
    {
        this.view = view;
        this.campaign = campaign;
    }

    public ValueTask InitializeAsync(CancellationToken token) => default;

    public ValueTask PrepareAsync(
        CampaignMapRoute route,
        ScreenPreparationContext preparation,
        CancellationToken token)
    {
        view.Render(campaign.GetTitle(route.ChapterId));
        return default;
    }

    public ValueTask ActivateAsync(
        CampaignMapRoute route,
        ScreenActivityContext activity)
    {
        next = async () =>
        {
            try
            {
                await activity.Navigation.PushAsync(
                    new CampaignMapRoute(route.ChapterId + 1));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        };
        back = async () =>
        {
            try
            {
                await activity.Navigation.BackAsync();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        };
        view.NextChapter.onClick.AddListener(next);
        view.Back.onClick.AddListener(back);
        return default;
    }

    public ValueTask DeactivateAsync()
    {
        if (next is not null)
        {
            view.NextChapter.onClick.RemoveListener(next);
            next = null;
        }
        if (back is not null)
        {
            view.Back.onClick.RemoveListener(back);
            back = null;
        }
        return default;
    }

    public ValueTask TerminateAsync() => DeactivateAsync();
}
```

CampaignMapView と CampaignService はゲームの型、UnityAction は Unity のイベント型である。共通契約自体は Unity を参照しない。入力のために Button.interactable を切り替える必要はなく、購読を活動期間だけ保持する。

Runtime が呼び出し直前と正常完了後にキャンセルを確認するため、各ライフサイクル実装の入口で ThrowIfCancellationRequested を繰り返す必要はない。非同期ロードなどを行う場合は、その処理へ渡された token を引き渡す。長時間の計算では処理途中のキャンセルにも対応する。DeactivateAsync と TerminateAsync はキャンセル後も呼ばれる停止処理であり、省略しない。詳細は[キャンセル契約](navigathena.md#非同期拡張処理のキャンセル契約)を参照。

Unity のイベント境界では、非同期操作を待って例外を捕捉する。この例ではログへ報告する。ゲーム側で復旧画面などを用意する場合は、そのエラー処理へ接続する。

## メニューを退避して編集 HUD へ切り替える

通常 HUD、メニュー、編集 HUD が同じ履歴で出入りするなら、一つの Layered Region に登録する。ヘッダーやボタンなど画面と一緒に出入りする部品は、その Screen に含める。マップ描画は UI の表示制御対象から分ける。同じ Scene の資源であっても、ScreenPresentation に接続する表示範囲まで同一にする必要はない。

```csharp
public sealed record GameHudRoute : Route;
public sealed record MenuRoute : Route;
public sealed record MapEditRoute : Route;

public static ScreenCatalog CreateUiCatalog(
    ScreenDefinition<GameHudRoute> gameHud,
    ScreenDefinition<MenuRoute> menu,
    ScreenDefinition<MapEditRoute> mapEdit)
{
    return ScreenCatalog.Build(
        new RegionDefinitionId("game-ui"),
        RegionCompositionMode.Layered,
        screens =>
        {
            screens.Register(gameHud, route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Reset;
                route.LowerPresentationPolicy = LowerPresentationPolicy.Preserve;
            });
            screens.Register(menu, route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Push;
                route.LowerPresentationPolicy = LowerPresentationPolicy.BlockInput;
            });
            screens.Register(mapEdit, route =>
            {
                route.AllowedEntryOperations = RouteEntryOperations.Push;
                route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRetain;
            });
        });
}
```

CreateUiCatalog と三つの Route はゲーム側のコードである。ScreenDefinition はゲームの構成処理が用意する。編集 HUD の例では、Inspector に設定した MapEditView 型の mapEditPrefab と、ゲーム側の mapEditing サービスから次のように構築する。

```csharp
var mapEdit = new ScreenDefinition<MapEditRoute>(
    async (creation, token) =>
    {
        var view = await creation.InstantiateScreenAsync(mapEditPrefab, token);
        return creation.Resources.CreateOwned(
            () => new MapEditPresenter(view, mapEditing));
    });
```

MapEditPresenter は IScreenLifecycleHandler<MapEditRoute> を実装するゲーム側の型である。Prefab には ScreenPresentation、表示・入力アダプター、必要なアニメーション担当を設定する。構築処理やメニューから、下位の View を取得して Hide を呼ばない。

メニューの編集ボタンでは、メニューの ActivateAsync が受け取った活動中の context を使用する。

```csharp
await activity.Navigation.PushAsync(new MapEditRoute());
```

編集 HUD の戻るボタンでは、その画面の活動 context を使用する。

```csharp
await activity.Navigation.BackAsync();
```

これらは操作イベントの処理であり、ActivateAsync 自体から遷移完了を待たせるものではない。イベントの購読は活動停止時に解除し、例外をイベント境界で処理する。

| Screen | メニュー表示中 | 編集中 | Back 後 |
| --- | --- | --- | --- |
| 通常 HUD | 表示・活動停止 | 非表示・保持 | 表示・活動停止 |
| メニュー | 表示・活動中 | 非表示・保持 | 表示・活動再開 |
| 編集 HUD | なし | 表示・活動中 | 履歴から除去 |

標準の復帰では同じメニュー実体の PrepareAsync を繰り返さず、新しい活動 context で ActivateAsync を呼ぶ。明示的な再準備の指定や実体解放がある場合は、後述の復帰設定と保存状態の契約に従う。Back は下位を無条件に活動開始せず、メニューによる通常 HUD の入力遮断を維持する。回答を要求する処理ではないため InvokeAsync を使わない。

独立した左右の Region がある場合、左への Push は右や親を隠さない。両方を退避したいなら、両 Region を所有する項目がある上位 Region へ Push する。画面側からは GetRegionNavigation(RegionTarget.AncestorRegion(...)) でその操作先を明示できる。退避対象の名前一覧や、新しいグループ管理オブジェクトは不要である。

## Back：元の Route と保存状態へ戻る

戻り先の標準動作は構築定義へ設定する。

```csharp
var screen = new ScreenDefinition<CampaignMapRoute>(CreateCampaignMapAsync)
{
    HistoryReturn = new ScreenHistoryReturnOptions
    {
        Preparation = ScreenPreparationMode.Always,
        EnterAnimation = ScreenEnterAnimationMode.WhenShown
    }
};
```

CreateCampaignMapAsync はゲーム側の構築処理。Always は同じ実体にも PrepareAsync を呼ぶ指定で、Scene や DI スコープを作り直す指定ではない。既定の WhenRequired は同じ項目の準備済み実体を保持する。

```csharp
// 通常は戻り先の定義に従う。
await activity.Navigation.BackAsync();

// 今回だけ再準備し、画面の入場アニメーションを省く。
await activity.Navigation.BackAsync(new BackOptions
{
    Preparation = ScreenPreparationMode.Always,
    EnterAnimation = ScreenEnterAnimationMode.Skip
});
```

PrepareAsync には元の Route と ScreenPreparationContext.SavedState が渡る。最新の保存値は同じハンドラーの IScreenStateCapture.CaptureState から取得する。ActivateAsync には元の Route と新しい活動 context が渡り、Reason は HistoryReturn になる。PreparationReason が null なら準備を維持した活動再開である。

IsFirstActivation は履歴項目の初回を表す。実体を再生成しても同じ項目なら false、新しい項目なら true。詳細は[履歴復帰の契約](../../50_tickets/technical/navigathena_history_reentry.md)を参照する。

## InvokeAsync：画面を使って回答を得る

通常画面は Route、回答付き画面は Route<TResult> から定義する。両者は兄弟型なので、回答付き Route を通常の Push、Replace、Reset、起動画面へ渡せない。

```csharp
public sealed record ConfirmRoute : Route<bool>
{
    public ConfirmRoute(string message) => Message = message;
    public string Message { get; }
}
```

画面のイベント処理から、現在の activity.Navigation を使って直接呼び出す。StartWork は必須ではない。

```csharp
// ライフサイクルの外で実行するイベント処理。
// token は今回の処理の取消。activity.CancellationToken ではない。
private async Task ConfirmAsync(
    IScreenNavigation navigation,
    CancellationToken cancellationToken)
{
    bool accepted = await navigation.InvokeAsync(
        new ConfirmRoute("この設定を適用しますか？"),
        cancellationToken);

    view.ShowAnswer(accepted);
}
```

InvokeAsync は、呼び出し元の履歴項目と画面実体に結び付いた Call を受理する。確認画面が開いて元画面の Activity が終わっても、その Call は存続する。回答待ちの間、呼び出し元と依存する実体は保持され、別の履歴項目へ再利用されない。所有元を除去すると待機は取消で終了し、その画面を再活動させない。

正常完了より先に、呼び出し先の必須終了処理と、戻り先の PrepareAsync・ActivateAsync・入力再開が完了する。回答は TResult として受け取り、復帰時の Route に混ぜない。

古い Activity の Navigation は新しい活動では使えない。復帰後に別の遷移を要求する場合は、再度 ActivateAsync で受け取った現在の Navigation を使用する。受理済みの Call が生きることと、古い入力 callback が新規要求を出せることは別である。

Runtime が所有するのは Call の終了までであり、await 後に続く任意のゲーム処理全体ではない。その処理の所有者が取消と終了待ちを行い、必要なら TerminateAsync に接続する。DeactivateAsync で回答待ち全体の終了を待つと、自分が開いた画面への遷移を妨げるため行わない。

回答側は IScreenLifecycleHandler<ConfirmRoute, bool> を実装する。PrepareAsync は通常画面と同じ責務で、ActivateAsync の context だけが回答型を持つ。

```csharp
public ValueTask ActivateAsync(
    ConfirmRoute route,
    ScreenActivityContext<bool> activity)
{
    commands = view.SubscribeChoices(
        () => activity.Call.Complete(true),
        () => activity.Call.Complete(false),
        () => activity.Call.Dismiss());
    return default;
}

public ValueTask DeactivateAsync()
{
    commands?.Dispose();
    commands = null;
    return default;
}
```

commands は IDisposable 型の購読、view.SubscribeChoices は三つのボタンを接続して購読を返すゲーム側の API。Complete と Dismiss はイベントから終了を要求し、その画面自身で終了完了を await しない。

false や null も正常な回答で、そのまま TResult として返る。回答せず Back または Dismiss した場合、外部 token や所有元が終了した場合は OperationCanceledException となり、後続の成功処理へ進まない。ゲーム上の「いいえ」「後で」を正常な回答として扱うなら Complete でその値を返す。構築・復帰・終了の失敗は例外になる。ScreenCallException は Stage、AnswerCommitted、FinalSnapshot と元の例外を持つ。

回答付き画面から詳細を Navigation.Push して Back しても、外側の回答待ちは続く。同じ回答型の別画面へ進む場合は Call.Replace / Call.Reset を使う。外側の履歴を終了させる場合は GetRegionNavigation で対象を明示する。

登録は ScreenDefinition<ConfirmRoute, bool> を使い、一つの型付きハンドラーを返す。DI なしでは Resources.CreateOwned、Microsoft DI では CreateScope と AddScreenLifecycleHandler、VContainer では CreateScope と RegisterScreenLifecycleHandler を使う。通常画面と同じく Route は DI 登録せず、画面実体のスコープを使い続ける。三方式の実行例は [ScreenScopeUnityTests](../../../tests/Unity/Assets/Tests/Editor/ScreenScopeUnityTests.cs) を参照する。

## 通常画面が閉じるまで待つ

```csharp
// LeaderboardRoute : Route。回答型や返却処理は不要。
await activity.Navigation.InvokeAsync(
    new LeaderboardRoute(),
    cancellationToken);
```

通常 Route に対する InvokeAsync は Task を返す。Back で呼び出し範囲が閉じ、必要な終了処理と元画面の復帰が終わると成功する。PushAsync は「開く遷移」の完了までを待つので、終了待機とは区別する。

業務フローの順序、イベントの二重実行抑制、スレッドの選択はゲーム側の関心である。Navigathena は汎用のコマンドや実行スケジューラーを提供しない。Unity の構築・遷移・終了は Unity のメインスレッドから呼ぶ。Host に SynchronizationContext を設定する必要はない。自分で Task.Run や ConfigureAwait(false) を使った処理は、Unity オブジェクトを操作する前にゲーム側で必要な実行先へ戻す。

画面に任意の処理全体を明示的に所有させ、終了まで View や DI スコープを保持する場合だけ、任意の StartWork を使える。Work は遷移成功後に開始され、画面終了時に取消と完了待ちの対象となる。Invoke のためだけに Work を作る必要はなく、画面より長く続くゲーム進行を Work に移さない。

## Reload：同じ訪問の実体を作り直す

```csharp
// Route と履歴項目を維持し、View・ハンドラー・DI スコープを再生成する。
await activity.Navigation.ReloadAsync();

// 表示状態も復元したい場合だけ指定する。
await activity.Navigation.ReloadAsync(
    new ReloadOptions
    {
        RestoreState = true
    });
```

既定では保存表示を適用せず、同じ Route から新しく準備する。RestoreState は IScreenStateCapture で保存した値を再準備へ渡す。子 Region の履歴も残し、子の実体は新しい親の構築登録から作り直す。PrepareAsync と ActivateAsync の理由は Reload、履歴項目の IsFirstActivation は false になる。

Single は旧実体の終了を待ってから再生成する。Multiple は独立した新旧実体を許し、Reload 完了までに旧実体の終了も待つ。同時表示必須の演出と Single の再生成が矛盾する場合は、表示を変更する前に拒否する。呼び出し元として Call や Work が実体を使用中の Reload も拒否する。

回答する側の画面を Reload しても Call は同じままで、新しい活動に回答権限を渡す。旧 View の callback からの回答は拒否する。

## 開始画面へ戻る・ゲームを再プレイする

新しいプレイを始める場合は、履歴を維持する Reload ではなく、Replace または Reset で新しい訪問を作る。View・ハンドラー・画面専用 DI スコープも作り直す場合は NavigationOptions.RecreateInstance を指定する。
以下の BootRoute、GameRoute、GameplayHudRoute、Regions はゲーム側の定義である。例はそれぞれ別の操作を示す。

### Host を維持して開始画面へ戻る

```csharp
var root = activity.Navigation.GetRegionNavigation(RegionTarget.Root);

await root.ResetAsync(
    new BootRoute(),
    new NavigationOptions
    {
        RecreateInstance = true
    });
```

Root Region の旧履歴とその子画面を終了し、新しい Boot の訪問と実体を作る。ゲーム側が所有する Root Scene・共通サービス・Host は終了しない。StartAsync を再実行する操作ではない。

### 履歴の途中にある Game から置き換える

```csharp
var root = activity.Navigation.GetRegionNavigation(RegionTarget.Root);

await root.ReplaceFromAsync<GameRoute>(
    new GameRoute(),
    new NavigationOptions
    {
        RecreateInstance = true
    });
```

Title → Game① → Result → Confirm を Title → Game②へ一つの操作で変更する。Game①自身、その上の画面、それらが所有する子 Region を終了する。Back を繰り返さず、Game①を途中で活動再開させない。

対象の GameRoute は、指定 Region の履歴にちょうど一件ある必要がある。一致なし・複数一致は画面を変更する前に拒否する。正確な訪問を指定する場合は HistoryTarget.Entry(entryId) を使う。親や子 Region を暗黙に検索しない。

初期子画面を指定する場合は、同じ基本操作へ目的地ツリーを渡す。

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

通常の ReplaceAsync は HistoryTarget.Current を使う拡張メソッドである。Game が現在の画面なら、ReplaceAsync(new GameRoute(), options) でよい。回答付き画面から外側の履歴を終了する場合も、上例の GetRegionNavigation で対象を明示する。除去される Call は取消になり、除去された呼び出し元は再開しない。

### 再構築するものと維持するもの

RecreateInstance は目的地とその初期子画面の構築処理を再実行する。旧訪問の保存状態や子履歴を新しい訪問へコピーしない。Single では旧実体の終了後に新しい実体を作る。新旧同時表示が必須の演出とは併用できない。

所有する Prefab・Scene は解放して取得し直す。配置済み View や共通サービスの借用は、同じ外部オブジェクトへ接続し直す。外部 Scene のアンロードや共通コンテナの再生成を意味しない。指定しなければ既存の実体再利用方針に従う。

明示した履歴境界からの置換、RecreateInstance を指定した遷移、Reload は、除去した実体の必須終了も待って正常完了する。終了失敗を成功フラグに隠さず例外で通知する。終了する画面自身の TerminateAsync や、その画面が終了待ちする Work から、この完了を待たない。

## 遷移の完了と取消

PushAsync・ReplaceAsync・ReplaceFromAsync・ResetAsync・BackAsync・ReloadAsync・StartAsync は Task を返す。正常完了は遷移が成立したことを表し、拒否・競合・実行失敗は例外になる。戻り先のない BackAsync も正常完了しない。

cancellationToken は操作の取消を要求する。確定前なら停止と巻き戻しを待って OperationCanceledException になり、先行解放や実体の書き換えで不可逆境界を越えた場合は、安全な完了または復旧まで待つ。成功後に遅れた取消だけを理由として取消結果を返さない。

操作の診断・複数待機・待機だけの取消が必要な場合は、Push 等が返す NavigationOperation を明示的に使う。WaitAsync の token は待機だけを取り消す。通常利用で NavigationResult.Kind や DestinationCommitted の検査は必要ない。

## ライフサイクル内から通常遷移を予約する

```csharp
public ValueTask ActivateAsync(BootRoute route, ScreenActivityContext activity)
{
    activity.Navigation.PostReplace(new TitleRoute());
    return default;
}
```

BootRoute と TitleRoute はゲームの通常 Route。PostPush / PostReplace / PostReset / PostBack は現在の遷移成功後に実行される要求で、戻り値を待たない。失敗した活動からの予約は実行しない。実行時の拒否や失敗は Runtime が監督する。

ライフサイクル中の PushAsync、InvokeAsync、Host.Client による直接遷移は拒否される。通常のイベント処理では活動開始後に InvokeAsync を呼ぶ。ライフサイクルから通常遷移を予約する場合は Post、任意の処理全体を画面所有で開始する場合だけ StartWork を使う。

## DI なし：取得後に生成して返す

```csharp
var screen = new ScreenDefinition<CampaignMapRoute>(
    async (creation, token) =>
    {
        var installer = await creation.LoadScreenAsync<CampaignMapInstaller>(
            new AddressablesSceneAcquisition(campaignScene), token);

        var view = installer.View;
        return creation.Resources.CreateOwned(
            () => new CampaignMapPresenter(view, campaignService));
    });
```

ScreenDefinition は共通 API。
LoadScreenAsync は ScreenCreationContext に対する Unity アダプターの拡張であり、取得後に表示構成を接続する。
AddressablesSceneAcquisition は Addressables アダプターの取得実装である。
Scene ロード完了後にその Scene 内の Installer を取得するため、起動時に遷移先 View の参照を要求しない。
この例では Installer と同じ GameObject に ScreenPresentation を置き、画面の表示アダプターと演出コンポーネントを Inspector で指定する。

CreateOwned はオブジェクトの所有を登録する。構築処理が返した一つのハンドラーがライフサイクルの入口になる。返すことで破棄所有を追加しない。既に取得アダプターが所有するオブジェクトをさらに CreateOwned へ渡さない。

## Microsoft DI：画面実体専用の登録とスコープ

```csharp
using MackySoft.Navigathena.MicrosoftDI;
using Microsoft.Extensions.DependencyInjection;

var screen = new ScreenDefinition<CampaignMapRoute>(
    async (creation, token) =>
    {
        var installer = await creation.LoadScreenAsync<CampaignMapInstaller>(
            new AddressablesSceneAcquisition(campaignScene), token);

        return creation.CreateScope(services =>
        {
            services.ImportService<CampaignService>(applicationProvider);
            installer.ConfigureServices(services);
        });
    });

// Scene に配置されたゲーム側 Installer のメソッド。
public void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton(view);
    services.AddScreenLifecycleHandler<CampaignMapPresenter>();
}
```

applicationProvider はゲーム側が作って持つ Provider。ImportService はそこから共通サービスを借用し、画面側で破棄しない。view の既存インスタンス登録も同様で、Scene の所有者は共通 Resources のままである。

CreateScope は独立した登録集合から Provider と実行スコープを作り、指定された一つのハンドラーを返す。AddScreenLifecycleHandler は画面の入口を一つ指定し、具体型を通常の DI 解決に登録する。利用者が Activator を別途呼び出す必要はない。画面内のサービスには通常の AddScoped 等を使用できる。ハンドラーの未指定、複数指定、Route 型不一致は構成エラーになる。

## VContainer：指定した親と標準 Installer

```csharp
using MackySoft.Navigathena.VContainer;
using VContainer;
using VContainer.Unity;

var screen = new ScreenDefinition<CampaignMapRoute>(
    async (creation, token) =>
    {
        var installer = await creation.LoadScreenAsync<CampaignMapInstaller>(
            new AddressablesSceneAcquisition(campaignScene), token);

        return creation.CreateScope(applicationResolver, installer);
    });

// Scene に置くゲーム側コンポーネント。
public sealed class CampaignMapInstaller : MonoBehaviour, IInstaller
{
    [SerializeField] private CampaignMapView view = null!;
    public CampaignMapView View => view;

    public void Install(IContainerBuilder builder)
    {
        builder.RegisterInstance(view);
        builder.RegisterScreenLifecycleHandler<CampaignMapPresenter>();
    }
}
```

applicationResolver はゲーム側の共通コンテナ。Installer は Inspector 参照と登録のみを持ち、LifetimeScope を作ったり破棄したりしない。CreateScope は子スコープから一つのハンドラーを解決して返す。子スコープは画面実体の終了時に解放される。共通サービスは VContainer の通常の親解決を使用する。

VContainer の非同期破棄を追加実装しない。非同期停止は TerminateAsync までに完了させ、所有オブジェクトの解放には標準の同期 Dispose を使う。

## 定義と Host の組み立て

上記の screen は、次の登録にそのまま渡す。

```csharp
var catalog = ScreenCatalog.Build(screens =>
{
    screens.Register(screen, route =>
    {
        route.AllowedEntryOperations =
            RouteEntryOperations.Push |
            RouteEntryOperations.Replace |
            RouteEntryOperations.Reset;
        route.LowerPresentationPolicy =
            LowerPresentationPolicy.HideAndRetain;
    });
});

var host = NavigationHost.Create(catalog);
await host.StartAsync(new CampaignMapRoute(1));
```

screens は ScreenCatalogDefinitionBuilder。子 Region を定義した場合は、同じ callback の RegisterScreens で子の構築方法も登録できる。既に作成した NavigationDefinition を使う Build では、構築方法の登録専用の ScreenCatalogBuilder を受け取る。

Unity メインスレッドで Host を作り、ゲームの構成側が保持する。Unity 専用の Host や UI 登録のための大域的な初期化 API は挟まない。

### Host の寿命はゲーム側の所有者が決める

Host はアプリケーション全体で使い続けても、ある機能を利用する期間だけ所有してもよい。常駐 Root Scene、DontDestroyOnLoad、通常の C# オブジェクトのどれに保持するかは利用側が選ぶ。Root Region は論理的な履歴であり、Unity の Root Scene とは別である。

同じ Host で開始地点から画面進行をやり直すなら、Host.Client.ResetAsync(host.Root, destination, options) を使う。画面からは activity.Navigation を使い、終了済み画面に操作権限を残さない。

Host 自体を作り直す場合は、画面外の所有者が ShutdownAsync の正常完了を待ってから新しい Host を生成・StartAsync する。終了した Host は再起動しない。共通サービスも作り直すか、同じものを次の Host へ渡すかはゲーム側が決める。Host の終了だけでは借用した View や外部 Scene を破棄しない。

## 単一実体と履歴

ScreenDefinition の標準は Single。RecreateInstance を指定しない場合、同じ定義の章1から章2へ Push しても、構築処理・Scene・View・DI スコープは増えず、同じ実体へ章2を準備する。Back では章1を再準備する。Replace は現在項目を置き換え、戻り先を増やさない。

復元値が必要ならライフサイクル実装が IScreenStateCapture を実装する。CaptureState が返した不変値は履歴項目に保存され、PrepareAsync の preparation.SavedState から受け取れる。null を返した場合は以前の保存値を消す。DI サービス内の状態が自動保存されるわけではない。

同じ項目がモーダルの背後から活動再開するだけなら、PrepareAsync は呼ばれない。新しい ScreenActivityContext で ActivateAsync が呼ばれ、古い活動からの要求は拒否される。

独立した二画面を同時表示する場合は、独立した取得・解放を保証する構築処理に ScreenInstancePolicy.Multiple を指定する。配置済みの単一 View を使う定義では指定しない。同じ取得元の登録には同じ定義を共有し、選択 callback 内で毎回定義を作り直さない。

親を別項目へ再利用する際は旧項目の子実体を終了し、子の履歴と保存状態を保持する。戻ったときはその履歴に対応する子実体を復元する。

## Prefab と既存 View

Scene 以外でも、その取得部分だけを変更する。

```csharp
// Inspector で指定した Prefab から独立した実体を生成する。
var view = await creation.InstantiateScreenAsync(prefab, token);

// 外部所有者が用意した既存 View を借用する。
var existing = await creation.BorrowScreenAsync(
    existingViewReference,
    ScreenAnimationState.Foreground,
    token);
```

既存 View の所有者は ResourceLifetime.Reference(view) を渡す。終了前には lifetime.EndAsync を待ち、その後に Scene や GameObject を自分で破棄する。View の登録だけでは外部所有の終了を代行しない。

Prefab の参照は画面ルート上の Component 型を使用する。
その GameObject に ScreenPresentation を置き、表示・入力アダプターと省略可能な AnimatorScreenAnimationDriver を設定する。
ゲームの View に NavigationView や ScreenAnimator プロパティを追加する必要はない。
BorrowScreenAsync の終了では取得時の表示・入力・順序と、指定した演出状態を返し、GameObject を破棄しない。
汎用の Resources.InstantiatePrefabAsync / LoadSceneAsync は資源取得だけを行い、画面を接続しない。

## 共通の選択ポップアップの登録

ゲームの共通 UI に Route・View・Presenter・Prefab を配置し、ゲーム全体の構成処理から対象 Region に登録する。
Prefab の共有は単一実体の指定ではなく、重ねて開く場合は Multiple を使う。

```csharp
public sealed record ConfirmationRoute(
    string Title,
    string Message,
    string AcceptText,
    string DeclineText) : Route<bool>;

// ゲーム側の構成処理。Prefab 参照は Inspector で設定する。
root.AddRoute<ConfirmationRoute>(route =>
{
    route.AllowedEntryOperations = RouteEntryOperations.Push;
    route.LowerPresentationPolicy = LowerPresentationPolicy.BlockInput;
});

screens.RegisterScreen(new ScreenDefinition<ConfirmationRoute, bool>(
    async (creation, token) =>
    {
        var view = await creation.InstantiateScreenAsync(
            confirmationPrefab, token);
        return creation.Resources.CreateOwned(
            () => new ConfirmationPresenter(view));
    },
    ScreenInstancePolicy.Multiple));
```

root は NavigationDefinition.Build の RootRegionDefinitionBuilder、screens は ScreenCatalog.Build 内の RegisterScreens へ渡される RegionScreenCatalogBuilder である。
登録時には生成せず、InvokeAsync の要求を受けて構築する。
回答は Presenter の activity.Call.Complete で返し、生成・演出・活動・解放は Runtime が管理する。
実際の構成例は Unity サンプルの ConfirmationPopupRegistration にある。

## 画面のアニメーション

ScreenPresentation の演出参照に AnimatorScreenAnimationDriver を設定する。
専用 Animator の入場・退避・復帰・退出ステートと、即時反映する各表示状態のステートを Inspector で指定する。
ステートは完全なパスを指定し、有限・非ループのクリップとし、自動遷移を設定しない。
既定の名前は PushIn / PushOut / PopIn / PopOut と BeforeEnter / Foreground / Background / Hidden / AfterExit である。
再生は非スケール時間で行い、位置・透明度などの演出対象を Canvas / GraphicRaycaster の管理ゲートと分ける。
詳細と独自アダプターの接続契約は[画面の表示構成と演出](navigathena_screen_presentation.md)に従う。

## 起動時の既存オーバーレイ

起動 Scene の黒幕をそのまま借用し、ロード前から覆った状態で使用する。

```csharp
var overlayLifetime = new ResourceLifetime();
var overlayReference = overlayLifetime.Reference(startupOverlay);
var reveal = new NavigationTransition(
    NavigationTransitionScope.Host,
    async (preparation, token) =>
    {
        var overlay = await preparation.Resources.BorrowAsync(overlayReference, token);
        preparation.RegisterExistingViewAdapter(overlay.NavigationView);
        return preparation.Resources.CreateOwned(() => new StartupRevealEffect(overlay));
    });

var operation = host.Start(
    new CampaignMapRoute(1),
    new NavigationOptions { Transition = reveal });
var result = await operation.WaitAsync();
```

StartupRevealEffect はゲーム側の演出。BeginAsync で黒幕を維持し、AfterCommitAsync でフェードアウトし、SettleAsync で成功・失敗時の状態を決める。完了後に画面の活動と入力を開始する。演出は Scene をロードせず、画面ライフサイクルを呼ばない。

遷移先 Scene 内の演出 View を使う場合は、その Scene を取得した構築処理で SetTransitionEffect を登録し、Region のルールに NavigationTransition.FromDestinationScreen を指定する。演出オブジェクトの所有は画面の CreateOwned または画面の DI スコープへ接続する。

## ブロッカーと DI

BlockerDefinition は、Resources を持つ BlockerPreparationContext と CancellationToken を受け取る非同期 factory をまとめる。同じ定義を複数箇所に登録すれば、同じ Host 内で一つの実体を使い回す。

共通定義の設定は一つでよい。構築時に Region を指定せず、SetScreenContext で現在接続されている画面と Region を受け取る。配置済み View は管理された借用で渡す。Region は矩形ではなく、全画面 View を自動的にクリップする機能ではない。

```csharp
var commonBlocker = new BlockerDefinition((preparation, token) =>
{
    return new ValueTask<IBlockerPresenter>(
        preparation.Resources.CreateOwned(() => new DefaultBlockerPresenter()));
});

var options = new NavigationHostOptions
{
    DefaultBlocker = commonBlocker
};

var inventoryBlocker = new BlockerDefinition((preparation, token) =>
{
    var provider = preparation.CreateScope(services =>
    {
        services.ImportService<InventoryService>(applicationProvider);
        services.AddScoped<InventoryBlockerPresenter>();
    });
    return new ValueTask<IBlockerPresenter>(
        provider.GetRequiredService<InventoryBlockerPresenter>());
});

screens.RegisterBlocker<InventoryRoute>(inventoryBlocker);
```

ここで screens は RegisterScreens callback の RegionScreenCatalogBuilder。DefaultBlockerPresenter、InventoryRoute、InventoryService、InventoryBlockerPresenter はゲームの型である。

IBlockerPresenter.PrepareAsync は取得済み依存からの準備、SetScreenContext は対象画面と説明・操作の接続、TerminateAsync は最終停止を担当する。画面 Presenter にブロッカーの破棄を書かない。演出とブロッカーの通常の DI 解決にも各構築 context の管理されたスコープを使え、Runtime とは別の寿命管理を作らない。

この例の共通・専用ブロッカーはカタログと Host の設定なので、画面を閉じても実体と DI スコープを保持し、Host 終了時に破棄する。親画面の Scene にある View を子画面用のブロッカーに使う場合は、その親の構築 callback 内で creation.RegisterScreens に定義を登録する。Runtime が親画面の資源より先にそのブロッカーを終了する。

別 Region で同時に二つのブロッカーを表示する必要がある場合は、それぞれ独立した View を取得する二つの BlockerDefinition を登録する。同じ定義の同時接続は拒否され、自動生成は増えない。別定義から同じ物理 View を返す登録も拒否される。

## 終了と結果の確認

```csharp
await host.ShutdownAsync();
await overlayLifetime.EndAsync();
await applicationProvider.DisposeAsync(); // Microsoft DI の場合。
// VContainer の共通コンテナなら applicationResolver.Dispose()。
```

画面実体の終了では、活動停止・最終停止を待ち、View の管理接続を解き、所有する Presenter・スコープ、表示準備資源、構築資源を解放する。借用したゲーム共通サービスはその後にゲーム側が終了する。await できない OnDestroy だけに全終了を任せない。

ShutdownAsync は全終了した場合だけ正常完了し、終了失敗は元の例外を含む AggregateException を送出する。結果のフラグを確認する必要はない。DisposeAsync も同じ終了処理を待つ。失敗時は Host と使用中の依存資源を保持し、共通サービスの破棄を無条件の finally に置かない。再度の ShutdownAsync と DisposeAsync は同じ試行と例外へ合流し、破棄を再実行しない。未完了・失敗した所有は Terminations.Current から確認できる。

NavigationResult は確定、要求の拒否、競合という通常の結果だけを返す。生成、準備、活動、演出などの実行失敗は NavigationException となり、InnerException に元の原因を保持する。設定不備は処理開始前に NavigationConfigurationException などで通知する。

NavigationException の DestinationCommitted、FinalSnapshot、PresentationStatus で失敗時の確定済み履歴と復旧の必要性を確認できる。例外を送出しても、確定済みの履歴を黙って巻き戻さない。

操作の取消が成立した場合は OperationCanceledException となる。結果待機の token は待機だけを取り消し、操作の取消は TryRequestCancellation を使用する。単一実体の書き換えが始まった後は取消を受け付けない。取消 callback の例外は TryRequestCancellation から伝わり、既に受理された取消要求自体は取り消さない。
