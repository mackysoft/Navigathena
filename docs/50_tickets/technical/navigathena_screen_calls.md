---
authority: proposal
status: implemented
---

# Navigathena：結果付き画面呼び出しと継続処理

## 目的と実装範囲

画面で回答を得る処理を、履歴操作、画面実体の寿命、活動期間と区別して管理する。
利用者は行き先、回答型、継続処理、操作対象を指定し、Runtime が取消、復帰、停止待ち、資源解放を調停する。
本書は共通 Runtime の画面呼び出し・継続処理の契約と受け入れ条件を定める。
通常の Back による履歴・復元・再入場は [履歴への復帰](navigathena_history_reentry.md) で扱い、回答の受信とは分ける。
既存の基本的な構築・DI API は [API と責務](../../30_technical/features/navigathena_class_design.md) と [使い方](../../30_technical/features/navigathena_usage.md) を参照する。

## 所有と責務

| 単位 | 管理対象 | 終了・失効 |
| --- | --- | --- |
| 履歴項目 | Route、保存状態、選択された構築定義、子履歴 | 履歴から除去されたとき |
| ScreenInstance | 一つのハンドラー、View、DI スコープ、取得資源 | 保持方針に従い、すべての使用と停止の完了後 |
| Activity | 一回の活動期間の購読、入力受付、Navigator | 非活動化。次回の活動には新しい権限を渡す |
| Call | 一回の画面呼び出し、回答型の有無、内部履歴の境界、待機先 | 回答または通常画面の終了、取消、障害のいずれか一つで終端する |
| Work | 履歴項目と画面実体の対応に所有される非同期処理 | 処理完了、所有する対応関係の終了、明示取消、実体の復旧不能な消失 |
| Region | 独立した履歴と画面構成 | 定義された履歴操作と所有元の終了 |

Call と Region は同一ではない。画面の表示形態が全画面かポップアップかは、回答の有無を決めない。
Unity、UI、Addressables、DI アダプターは具体的な取得・表示・解放を実装する。上記の所有と実行順序は共通 Runtime に置く。
常駐シーンや DontDestroyOnLoad を利用の前提にしない。

フローの順序・条件・回答の適用はゲーム側の関心とする。Presenter や既存のゲーム処理に通常の async メソッドとして書き、画面のイベント処理は現在の activity.Navigation.InvokeAsync を直接 await できる。処理全体の所有も Runtime へ接続する場合だけ StartWork を使う。Work はそのデリゲートの実行を管理する単位であり、ゲームロジックを代わりに実装する主体ではない。
画面より長く続くフローは、ゲーム側のより長い寿命の所有者が管理する。Host.Client が所有するのは Call であり、呼び出し元が await 後に行う任意の処理まで Host が管理するとは扱わない。

## 一画面一つの入口

ScreenDefinition の構築処理は IScreenLifecycleHandler を一つ返す。複数登録する RegisterLifecycleHandler は廃止する。
DI なしでは Resources.CreateOwned または取得アダプターで所有したハンドラーを返す。
DI ありでは画面専用スコープに一つのライフサイクル実装を指定し、通常の DI 解決で得た実体を返す。
未指定、複数指定、Route 契約の不一致は初期化前に構成エラーにする。
同じハンドラー実体を複数の生存中の画面実体の入口にはできない。

戻り値はライフサイクルの接続を表し、破棄所有を追加しない。
Runtime は Initialize、Prepare、Activate、Deactivate、Terminate を呼ぶ。オブジェクトの破棄は既存の資源所有者または DI スコープが一回だけ行う。
画面内部の複数 Presenter やサービスへの委譲は、その一つの入口が行う。
Route は Prepare と Activate の引数で渡し、コンストラクターや DI へ注入しない。
保存状態の取得も同じ入口に接続する。回答を受け取るためのライフサイクル参加者は追加しない。

## 型付きの画面呼び出し

通常の Route と Route<TResult> は共通基底 NavigationRoute の兄弟型にする。
通常の Push は Route のみを受け付ける。InvokeAsync は Route<TResult> の回答待機と、Route の終了待機を別のオーバーロードで受け付ける。
通常の Replace / Reset、起動、子 Region の初期構成、DestinationTree からも、返却先なしで結果付き Route を開けないようにする。
NavigationRoute は履歴・登録の共通表現であり、無制約に画面を開く公開入口にはしない。
結果型を推論する InvokeAsync の入力は Route<TResult> とし、型制約だけからの推論を要求しない。
Route の値が同じでも、呼び出しごとに Call の識別子を発行する。
結果付きの構築定義は ScreenDefinition<TRoute, TResult>、画面の入口は IScreenLifecycleHandler<TRoute, TResult> とし、TRoute は Route<TResult> に制約する。
結果付きの活動 context と Work context は TResult の返却権限を必須とする。通常表示用ハンドラーに任意の返却先を付け足す方式にはしない。

| 操作 | 意味 |
| --- | --- |
| Push / PushAsync | 通常表示。非同期版は遷移の完了を待ち、画面の回答を待たない |
| InvokeAsync(Route<TResult>) | 型付き回答を待つ。回答元の終了と必要な呼び出し元の復帰が成立してから成功させる |
| InvokeAsync(Route) | 回答なしの通常画面が閉じるまで待つ。終了処理と必要な呼び出し元の復帰後に成功させる |
| Call.Complete | 型付き回答を提出する。自身の停止・破棄完了を待たない |
| Call.Dismiss | 回答せず閉じる要求を提出し、回答待機を取消で終える |
| Call.Replace / Call.Reset | 同じ TResult の Route へ変更し、Call と外側の待機を維持する |

結果付き画面には必須の型付き返却権限を渡す。activity.Call が null かを通常画面と共通のハンドラーで判定させない。
ScreenActivityContext<TResult> の TResult は、その画面が外へ返す型であり、復帰時に受け取る型ではない。
Open、ScreenReturnPort、IScreenReturnHandler、回答をライフサイクルへ配送する専用経路は採用しない。
Call 内から通常の詳細画面を Push でき、詳細の Back は外側 Call を終了しない。
Call の開始境界から許可された Back は回答なし終了として扱う。内側の確認 Call の終了を、外側の選択 Call へ無条件に伝播させない。
Call 内の Replace は旧画面の Work と返却権限を終了し、新画面へ新しい権限を渡す。
回答主体を失う通常画面への差し替えは拒否する。

外側 Region への Reset 等は構成側で許可された対象に明示する。Runtime は除去される Call と Work を同じ変更として計算する。
対象 Region 用の Navigator を取得しても、要求元の履歴項目と世代の制約を失わない。
取消後に Reset を提出する二段階の操作を利用者へ要求しない。
除去される呼び出し元を途中で再活動させない。

## 回答、回答なし終了、取消、障害

InvokeAsync の署名は次のとおりとする。

```csharp
Task<TResult> InvokeAsync<TResult>(
    Route<TResult> route,
    CancellationToken cancellationToken = default,
    NavigationOptions? options = null);

Task InvokeAsync(
    Route route,
    CancellationToken cancellationToken = default,
    NavigationOptions? options = null);
```

型付き呼び出しは TResult を直接返す。結果ラッパーや状態識別子の確認を成功処理の前提にしない。障害を回答値へ入れず、回答のない終了を default(TResult) へ変換しない。

| 発生したこと | 待機への通知 |
| --- | --- |
| Complete(false)、Complete(null) など | 値をそのまま返す |
| 結果付き画面を回答せず Back、Call.Dismiss | OperationCanceledException。否定回答とは別 |
| 終了待機で開いた通常画面を Back | Task が正常完了する |
| 呼出し token または所有元の終了 | OperationCanceledException |
| 外側の Reset・範囲置換で呼び出し範囲を終了する | 取消。所有元も除去される場合は活動を再開しない |
| 構築・遷移・復帰・必須の終了処理の障害 | 段階と確定情報を持つ ScreenCallException |

「いいえ」「後で」もゲーム上の回答なら Complete で値を返す。回答なしで閉じる操作は、回答を必要とする処理の取消である。取消を扱わなければ後続の成功処理へ進まない。
通常画面の終了待機には、回答処理やダミーの結果型を追加しない。通常のライフサイクル実装と履歴操作を使い、Call の所有・終了・復帰は同じ Runtime で管理する。
Call.Complete / Dismiss は void の要求 API とし、不正な世代・重複回答は例外で拒否する。
正常に戻ったことは要求の受理を表す。回答側へ終了完了を待つ Task を渡さず、以後の失敗は Call の待機先と Runtime の監督へ伝える。

```csharp
// ゲーム側の結果付き Route と回答値。
public sealed record NameSettingRoute : Route<NameSettingResult>;
public sealed record NameSettingResult(string? DisplayName);

// IScreenLifecycleHandler<NameSettingRoute, NameSettingResult> の実装の一部。
public ValueTask ActivateAsync(
    NameSettingRoute route,
    ScreenActivityContext<NameSettingResult> activity)
{
    commands = view.SubscribeNameChosen(name =>
    {
        activity.Call.Complete(new NameSettingResult(name));
    });
    return default;
}
```

commands は DeactivateAsync で破棄するゲーム側の購読である。Complete の後で Back を追加しない。
古い活動で作った callback は新しい活動へ回答できない。Work から回答する場合も、現在の所有項目と Call に結び付いた型付き権限を使う。

## Work と待機の所有

Work は Activity が終わっても存続できる。これによりモーダルの背後で結果を待てる。
Work の存続中は、捕捉したハンドラー、View、依存サービスの実体を継続処理の終了まで保持する。
存続する別の履歴項目の Work が使用中の Single 実体を、新しい項目へ横取りして再利用しない。
所有元を実際に除去する操作では、取消と待機解除を通知し、Work の停止を待ってから再利用・解放する。

結果を受け取る経路は InvokeAsync の await に限定する。戻り先の ActivateAsync には元の Route と新しい活動 context を渡し、回答は渡さない。
画面所有の InvokeAsync は IScreenNavigation に置き、Activity.Navigation から直接呼べる。Call は発行元の履歴項目と実体に結び付き、Activity の停止だけでは取消しない。受理後の回答待ちは Work の有無で変わらない。旧 Activity が新しい遷移を発行する権限は復帰しない。
Host.Client の InvokeAsync は Host に所有される外部呼び出し用の入口とし、画面 callback からの所有制約の迂回には使えない。

StartWork はライフサイクル中には登録だけを行う。現在の活動開始を含む遷移が正常に完了してから、デリゲート全体を実行する。
活動開始失敗時は未開始処理を破棄する。非同期デリゲートをその場で呼び、最初の await までを実行してしまう実装にはしない。
自動的に一回だけ開始する処理には履歴項目単位の IsFirstActivation を使う。実体の再生成を、新しい訪問と誤認しない。
StartWork が返す ScreenWork は Cancel と WaitAsync を持つ。WaitAsync は回答後の続きも含めたデリゲート全体の終了を待ち、非同期購読・既存コマンドとの接続に使う。待機者の有無にかかわらず Runtime が終了を待ち、障害を観測する。
待機 token の取消は Work 自体を止めず、Cancel が処理の取消を要求する。ライフサイクル中の WaitAsync と、Work が自分自身を待つ操作は例外で拒否する。呼び出し側へ手動の生存フラグや終了待ちを必須にはしない。

```csharp
// ランキング画面の自動開始処理。
public ValueTask ActivateAsync(
    LeaderboardRoute route,
    ScreenActivityContext activity)
{
    if (activity.IsFirstActivation)
    {
        activity.StartWork(async work =>
        {
            NameSettingResult result = await work.Navigation.InvokeAsync(
                new NameSettingRoute(), work.CancellationToken);

            if (result.DisplayName is not null)
            {
                await account.SetDisplayNameAsync(
                    result.DisplayName, work.CancellationToken);
            }

            var ranking = await leaderboard.LoadAsync(
                route.BoardId, work.CancellationToken);

            view.Render(ranking);
        });
    }

    commands = view.SubscribeCommands(activity.Navigation);
    return default;
}
```

NameSettingRoute、NameSettingResult、account、leaderboard、view はゲーム側の型・依存である。
Work が完了してから同じ項目の実体が再生成される場合の表示復元は PrepareAsync が担当し、自動 Call を再実行しない。
InvokeAsync の取消後に処理を継続する場合はゲーム側で明示的に扱う。捕捉しなければ Work は取消で終了する。

await 後も古い Activity を使用できるとは保証しない。View の保持と Activity の有効性は別であり、Work は非活動中も保持された View を操作できる。現在の実行を所有する Work.Navigation で遷移を要求し、古い Activity の権限は再使用しない。
Work は Host に設定した SynchronizationContext で開始する。通常の await は同じ実行先を引き継ぐので、View 更新のためだけの専用ゲートを要求しない。Task.Run や ConfigureAwait(false) で実行先を変えた場合は、その実行先から UI を操作してよいかを利用側が管理する。
Work の token は、その Work が所有する Call の取消へ接続する。Activity の停止 token を回答待ちへ自動接続しない。
同一項目の再準備では Work を終了せず、参照中の旧準備資源を保持する。実行中の Work や await の続きを永続化しない。

同じ項目でも再準備によりハンドラーのフィールドは差し替わり得る。資源を保持することはフィールドの値を固定することではない。Work が安定した値を必要とする場合は、入力値・項目に属するモデル・長く生存するサービスを明示的に捕捉する。並行するゲーム処理の表示内容の調停はゲーム側が担当する。

ライフサイクル中の通常遷移には PostPush / PostReplace / PostReset / PostBack という void の要求専用入口を設ける。
これらも現在の遷移成功後に実行し、失敗時には登録を破棄する。Runtime は要求実行時に世代と操作権限を再検証し、拒否や障害を未観測にしない。
通常の PushAsync や Host.Client を使う迂回も、最終要求受付で拒否する。Post の登録だけは現在の活動開始中にも許可する。
Post と Work を複数登録したことを、複数遷移がすべて成功する保証にしない。同じ対象への競合には通常の予約規則を適用し、実行順が必要な処理は一つの Work の中で明示する。

## 復帰との接続

回答要求 → 回答元の入力・活動停止 → 回答元を除去する履歴と回答の同時確定 → 戻り先の復帰 → 必須の終了処理の完了 → InvokeAsync の正常完了、とする。
復帰には通常の履歴復帰計画を使う。Call 専用の復元・再入場ライフサイクルを作らない。
Task の継続は非同期に実行し、ロック・確定処理・遷移予約の保持中に結果を公開しない。

呼び出し元の旧準備資源を Work が保持している場合、その最終解放は Work 終了後に行う。呼び出し先の必須終了と異なり、旧準備資源の実解放を InvokeAsync の完了条件には含めない。回答待ちと資源解放待ちの相互待機を作らない。
戻り先の再準備・再活動は、回答を受け取った Work のモデル更新より先に完了する。回答後の更新を反映するために、回答値をライフサイクルへ別配送しない。
遷移完了の通知を終えてから予約済み Work を開始し、その Work が次の遷移を即座に要求しても前後の完了通知を逆転させない。

回答の確定と await の成功は別に追跡する。復帰に失敗すれば await は失敗するが、確定済み回答や履歴を未発生に戻さない。
ScreenCallException は CallId、Stage、AnswerCommitted、FinalSnapshot、元の例外を持つ。
回答後に所有元が終了した場合も回答の確定記録を変更せず、その所有元の継続処理は取消で終了させる。

## 実行境界と失敗

事前検証、旧実体の変更開始、履歴確定を区別する。
事前検証では操作権限、要求元の世代、結果型、構築定義の同時使用、遷移演出の要件を確認する。
非同期処理後は必要な世代を再検証する。

旧 View の使用を終える前に Single 実体を新しい Route へ書き換えない。
事前に別資源を準備できる場合と、旧実体の停止・解放を先に必要とする場合を区別する。
既存実体の書き換えや解放が始まった後は、履歴が未確定という理由だけで無変更や復旧成功とは扱わない。
失敗時は入力・活動を閉じ、保存状態からの復旧または復旧不能を報告する。

履歴、Call の終端、操作権限の失効、終了対象 Work の決定を整合させる。
Call は CallId、呼出し元の履歴項目 ID と実体世代または Host 所有、呼出し先の履歴範囲、ParentCallId を保持する。WorkId を所有の必須条件にしない。
Route 型、現在の最上位、物理実体 ID だけで呼び出しや待機先を識別しない。
同一 Region の呼び出しは有効な最上位から行い、復帰は履歴の逆順とする。別 Region への要求でも発行元の所有と権限を失わない。
Call 内の通常画面と子 Call も終了計画に含める。取消を Back 一回へ置き換えない。
取消コールバックや利用者の処理は、内部状態のロックを保持したまま実行しない。
子 Call の待機を解除してから親 Work の停止を待つ。
自己終了を提出した Work は自身の破棄完了を await しない。
停止に応答しない処理が使用する資源を、タイムアウトだけを理由に解放しない。
所有元の終了時には、活動・回答権限を失効させ、Call の待機を解除し、Work を取り消して終了を待った後、依存順に資源を解放する。
所有元が残る Call 単独取消では、呼び出し先の終了と戻り先の復帰も Runtime が行う。待機だけを取り消して画面を残さない。
取消と確定が競合した場合は一つの終端だけを採用し、採用済み回答を後から取消へ書き換えない。

## 共通 Runtime とアダプター

| 部分 | 決定権 |
| --- | --- |
| NavigationRuntime | 操作受付、履歴・復帰・終了対象の計画、共有資源の予約、履歴と Call の整合した確定 |
| ScreenRuntime | 実体、準備世代、活動、演出、取得資源、終了待機の実行 |
| Call 管理 | 呼出し ID、呼出し元の履歴項目・実体、親子関係、回答・終了状態、待機への通知。履歴を独立に変更しない |
| Work 管理 | 履歴項目の所有、開始の遅延、使用中資源の保持、取消、例外監督、停止待ち |
| Unity / UI / Addressables | Scene・GameObject・UI・取得解放の具体操作 |
| Microsoft DI / VContainer | 実体専用の構築・解決・スコープ終了を共通所有へ接続 |

DI の有無でライフサイクル、復帰、Call、Work の規則を変えない。
画面実体一つにつきハンドラーと DI スコープを一つ持ち、再活動・同一項目の再準備・結果返却ではスコープを作り直さない。
Route、Activity、回答待ちを DI の長寿命サービスとして登録しない。

## 実装と受け入れ条件

回答通知用の Open、ReturnPort、独立した受信ハンドラーは公開しない。CallCoordinator が await の終端を管理し、ScreenWorkCollection が活動期間より長い継続処理を所有する。
PreparedScreenPublication が共通の履歴復帰計画を実行する。同じ項目の再準備では Work と準備資源を保持し、別項目への書き換えと区別する。
通常・結果付きハンドラーは同じ所有基盤を使い、Microsoft DI と VContainer はその構築・終了へ接続する。

受け入れ条件は以下とする。型名を固定するための検査ではなく、利用コードの構築と外部から観測できる振る舞いを検証する。

- 結果付き Route を通常 Push / Replace / Reset / 起動 / 子初期構成へ渡せず、回答型の不一致もコンパイルで拒否される。
- 正常な false / null の回答、回答なしの Back による取消、外部 token の取消、処理例外を混同しない。
- 通常画面の終了待機は、入れ子の回答付き呼び出し・必須終了・呼び出し元の復帰を待ち、ダミーの回答を要求しない。
- Work の終了待機は回答後の処理も含み、待機の取消と処理の取消を分ける。ライフサイクル・自己待機を即時拒否する。
- 非活動中の保持 View と、通常 await 後の実行 context を維持し、専用の表示更新ゲートなしで処理できる。
- A → B → C の回答は C から B、B から A へ返り、通常の詳細 Push / Back では外側 Call を終了しない。
- Call 内の型付き Replace / Reset は Call を維持し、終了した実体の Work と古い回答権限は失効する。
- 外側 Reset は除去対象の子 Call を含めて終わらせ、終了する呼出し元を一時的にも再活動させない。
- 回答・Back・取消の競合、開く途中の取消で、二重確定・孤立画面・未完了待機が残らない。
- 回答確定後に呼出し元の活動開始や必須の資源解放が失敗すると、InvokeAsync は確定情報付き例外で終わる。
- 非協調的な Work の終了前に Presenter・View・DI スコープ・旧準備資源を解放しない。
- 再活動で自動 Work を重複起動せず、失敗した活動開始から登録された処理を起動しない。

汎用メッセージバス、公開ミドルウェア、受信先の登録、文字列キー付き Work の管理機構はこの改善の前提として追加しない。

## 実体再生成

ReloadAsync は同じ履歴項目・Route・Call を保ち、画面と子画面の実体を作り直す。Single は旧実体の終了を待つ。新旧が独立した Multiple では同時存在を許すが、旧実体の終了まで Reload の完了を待つ。保存表示の適用は ReloadOptions.RestoreState で明示する。

Call の回答側を Reload すると新しい活動へ同じ回答型の権限を渡し、古い回答権限を失効させる。呼び出し元として未完了の Call や Work が実体を使用している Reload は事前に拒否する。先行解放後に構築が失敗した場合は、元の Route と保存状態からの復旧を試み、成功でも Reload 自体は失敗として通知する。

## 関連する未解消項目

以下は単一入口の実装で解消したものとは扱わない。Call の導入後も、別の受け入れ条件として検証する。

- 既存 View の使用権移譲：寿命の借用と、表示・入力・イベント接続を独占的に制御する権限は別である。共有 HUD 等の借用中は元の制御を停止し、返却時は現在の画面構成に応じて復帰する。独占使用の調停は共通 Runtime、具体操作は UI アダプターに置く。
- Unity の取得・配置：標準 SceneManager による取得、ロード先 Scene 内の配置済み UI、Prefab の配置先、子 Canvas を含む表示接続を個別に検証する。取得 API の不足を画面ロジックによる手動の寿命管理で補わせない。

## 検証状況

ScreenCallContractTests は回答・回答なし・取消・入れ子の呼び出し、詳細画面、所有元の終了、復帰失敗、遷移中の取消、再準備と資源保持、Post の成功後実行を検証する。
ScreenHistoryContractTests は単一実体の章切替・保存状態・子履歴の復元を検証する。
ScreenScopeUnityTests は実際の Canvas を使い、手動構築・Microsoft DI・VContainer の回答返却、再準備、元の項目への復帰、スコープの一回だけの終了を検証する。
