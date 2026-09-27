# MackySoft.Navigathena

エンジン非依存の画面管理ライブラリ。Region ごとの履歴、画面実体、型付きライフサイクル、資源所有、表示・入力、遷移演出、ブロッカー、復旧を共通 Runtime が管理する。

```csharp
var screen = new ScreenDefinition<TitleRoute>((creation, token) =>
{
    return new ValueTask<IScreenLifecycleHandler<TitleRoute>>(
        creation.Lifetime.CreateOwned(() => new TitlePresenter(titleService)));
});

var catalog = ScreenCatalog.Build(definition, builder =>
    builder.RegisterScreens(rootRegion, screens => screens.RegisterScreen(screen)));

await using var host = NavigationHost.Create(catalog);
await host.StartAsync(new TitleRoute());
```

TitleRoute、TitlePresenter、titleService は利用者の型・依存。
構築処理が返す一つの Presenter が画面の入口になる。IScreenLifecycleHandler<TitleRoute> を実装し、Route を PrepareAsync と ActivateAsync の引数で受け取る。構築時のコンストラクターへ Route を渡さない。

`creation.Lifetime` は画面実体、`preparation.Lifetime` は今回の表示準備に対応する所有・借用の登録窓口。共通の `LifetimeContext` が `CreateOwned`、`AcquireAsync`、`BorrowAsync` を提供し、Runtime が必要な停止を待って解放する。DI が所有するオブジェクトを重ねて `CreateOwned` に登録しない。Unity の `Resources.Load` とは別の API である。

ScreenDefinition の標準は Single。同じ定義の新しい履歴項目には通常は実体を再利用し、履歴と View・DI スコープを分離する。独立した取得・解放を保証する定義には Multiple を指定する。

開始地点へ戻る場合は Reset、履歴内の特定画面から上側を置き換える場合は ReplaceFromAsync を使う。NavigationOptions.RecreateInstance を指定すれば、目的地と初期子画面の実体・画面スコープも作り直す。Reload は新しい訪問を作らず、現在の Route と子履歴を保って実体を再構築する。

Host の寿命はゲーム側の所有者が決める。Root Region の Reset では Host や外部の共通サービスを終了しない。Host 自体を作り直す場合は ShutdownAsync の正常完了を待つ。借用する外部 View や Scene の寿命は、画面や Host の寿命とは別に扱う。

LowerPresentationPolicy は同じ Region の下位画面とその子構成へ作用する。HUD・メニュー・編集画面を同じ履歴に積み、編集画面を HideAndRetain にすると、下位 UI を保持したまま退避し、Back で元のメニューへ戻れる。親や別の Region を暗黙に隠さず、共有ブロッカーは同じ定義の実体を Region 間で使い回す。

## 遷移要求と待機

画面内では `ScreenActivityContext.Navigation` を使う。画面外から操作する場合は `host.Client` に対象の `RegionInstanceId` を渡す。

| API | 戻り値と完了条件 |
| --- | --- |
| `Push`、`Replace`、`Reset`、`Back`、`Reload` | 要求を出して `NavigationOperation` を返す。呼び出し時に遷移が完了するわけではない |
| `PushAsync`、`ReplaceAsync`、`ResetAsync`、`BackAsync`、`ReloadAsync` | 遷移完了を待つ `Task` を返す拡張メソッド |
| `InvokeAsync(Route, …)` | 呼び出した画面の終了・資源解放を待つ。画面内からの呼び出しは呼び出し元の活動再開も待つ |
| `InvokeAsync<TResult>(Route<TResult>, …)` | 同じ終了処理を待ってから `TResult` を返す。回答なしで閉じた場合は取消になる |

非同期の遷移要求は、対象、オプション、`CancellationToken` の順に指定する。オプションが不要なら名前付き引数でトークンだけを渡せる。

```csharp
await navigation.PushAsync(route, cancellationToken: cancellationToken);

// answerRoute は Route<TResult> を継承したゲーム側の Route。
var answer = await navigation.InvokeAsync(
    answerRoute,
    cancellationToken: cancellationToken);
```

操作を個別に観測・取消したい場合は `NavigationOperation` を使う。

```csharp
NavigationOperation operation = navigation.Push(route);
NavigationResult completed = await operation.WaitAsync(waitCancellationToken);
```

`WaitAsync` のトークンは待機だけを取り消す。遷移自体への取消要求は `operation.TryRequestCancellation()` で行う。`PushAsync` などのトークンは遷移の取消を要求し、終了処理が落ち着くまで待つ。

どちらの待機方法でも、要求の拒否・競合・実行失敗は `NavigationException`、取消は `OperationCanceledException` になる。`RecoverAsync` も拒否・競合・復旧失敗を例外で通知する。成功判定のために `NavigationResult.Kind` や `DestinationCommitted` を分岐させる必要はない。確定状態や復旧状況は例外から確認できる。

引数や構成の誤りは、要求の受理前に引数例外や `NavigationConfigurationException` になる。

`NavigationHostOptions.OperationCompleted` は観測用であり、Runtime が処理した要求の拒否・競合も通知される。画面を制御するコードは操作の完了を待ち、観測通知を成功判定の代わりに使わない。

.NET Standard 2.1 / C# 9 を対象とする。Microsoft DI と VContainer は任意の連携アダプター。Unity、uGUI、UI Toolkit、Addressables は具体的な取得と表示操作だけを担当する。

- [Unity・DI あり／なしの利用例](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples)
