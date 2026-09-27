# Navigathena Microsoft.Extensions.DependencyInjection adapter

`ScreenDefinition<TRoute>` の構築処理から `creation.CreateScope(services => …)` の戻り値を返す。
`services.AddScreenLifecycleHandler<Presenter>()` で画面の単一入口を指定し、通常のコンストラクター解決に登録する。
未指定、複数指定、Route 型不一致は初期化前に構成エラーとなる。
Route は登録せず、`PrepareAsync` と `ActivateAsync` の引数で受け取る。

結果付き画面は `IScreenLifecycleHandler<TRoute, TResult>` を実装する。
`ScreenCatalogDefinitionBuilder.Register<TRoute, TResult>` の構築処理でも、`ScreenDefinition<TRoute, TResult>` と同じ `ScreenCreationContext<TRoute, TResult>` が渡されるため、`creation.CreateScope(services => …)` をそのまま使える。

実体ごとに独立した登録・Provider・非同期実行スコープを作る。
`ImportService<T>(applicationProvider)` は既存インスタンスの借用であり、画面終了では解放しない。
アプリケーションの Provider は Host の終了完了後に破棄する。

同じ実体を別の履歴項目へ再利用しても、DI スコープを再構築しない。
ライフサイクル終了を共通 Runtime が待った後、スコープの非同期破棄を待ち、依存資源を解放する。
