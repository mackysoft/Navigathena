# MackySoft.Navigathena

エンジン非依存の画面管理ライブラリ。Region ごとの履歴、画面実体、型付きライフサイクル、資源所有、表示・入力、遷移演出、ブロッカー、復旧を共通 Runtime が管理する。

```csharp
var screen = new ScreenDefinition<TitleRoute>((creation, token) =>
{
    return new ValueTask<IScreenLifecycleHandler<TitleRoute>>(
        creation.Resources.CreateOwned(() => new TitlePresenter(titleService)));
});

var catalog = ScreenCatalog.Build(definition, builder =>
    builder.RegisterScreens(rootRegion, screens => screens.RegisterScreen(screen)));

await using var host = NavigationHost.Create(catalog);
await host.StartAsync(new TitleRoute());
```

TitleRoute、TitlePresenter、titleService は利用者の型・依存。
構築処理が返す一つの Presenter が画面の入口になる。IScreenLifecycleHandler<TitleRoute> を実装し、Route を PrepareAsync と ActivateAsync の引数で受け取る。構築時のコンストラクターへ Route を渡さない。

ScreenDefinition の標準は Single。同じ定義の新しい履歴項目には通常は実体を再利用し、履歴と View・DI スコープを分離する。独立した取得・解放を保証する定義には Multiple を指定する。

開始地点へ戻る場合は Reset、履歴内の特定画面から上側を置き換える場合は ReplaceFromAsync を使う。NavigationOptions.RecreateInstance を指定すれば、目的地と初期子画面の実体・画面スコープも作り直す。Reload は新しい訪問を作らず、現在の Route と子履歴を保って実体を再構築する。

Host の寿命はゲーム側の所有者が決める。Root Region の Reset では Host や外部の共通サービスを終了しない。Host 自体を作り直す場合は ShutdownAsync の正常完了を待つ。借用する外部 View や Scene の寿命は、画面や Host の寿命とは別に扱う。

LowerPresentationPolicy は同じ Region の下位画面とその子構成へ作用する。HUD・メニュー・編集画面を同じ履歴に積み、編集画面を HideAndRetain にすると、下位 UI を保持したまま退避し、Back で元のメニューへ戻れる。親や別の Region を暗黙に隠さず、共有ブロッカーは同じ定義の実体を Region 間で使い回す。

.NET Standard 2.1 / C# 9 を対象とする。Microsoft DI と VContainer は任意の連携アダプター。Unity、uGUI、UI Toolkit、Addressables は具体的な取得と表示操作だけを担当する。

- [Unity・DI あり／なしの利用例](https://github.com/mackysoft/Navigathena/tree/main/tests/Unity/Assets/Samples)
