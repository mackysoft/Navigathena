---
authority: proposal
status: implemented
---

# Navigathena：履歴への復帰と再入場

## 範囲

Back による履歴操作と、戻り先の表示準備・状態復元・活動再開を定める。
本書は共通 Runtime に実装する復帰契約と受け入れ条件を定める。利用例は [使い方](../../30_technical/features/navigathena_usage.md) を参照する。
結果付き画面の回答は [画面呼び出し](navigathena_screen_calls.md) の InvokeAsync で扱う。
Back、復帰先の Route、活動開始 context に回答値や返却先を追加しない。

## 履歴、実体、活動を区別する

| 単位 | 保持するもの | 同一性 |
| --- | --- | --- |
| 履歴項目 | Route、構築定義、保存状態、子履歴、活動開始済みか | NavigationEntryId |
| 画面実体 | 一つのライフサイクル実装、View、DI スコープ、取得資源 | ScreenInstance。Runtime 内部 |
| 表示準備 | 一つの項目を表示するために取得したデータ・資源 | 実体内の準備世代。Runtime 内部 |
| 活動 | 購読、入力受付、活動中だけ有効な Navigation | 活動世代。再開時に更新 |

Back は現在の履歴項目を除去し、既存の戻り先項目を選ぶ。新しい訪問を作らず、Route も置き換えない。
同じ Route 値の項目が複数あっても、履歴項目の ID で区別する。
戻り先が存在しない場合の拒否と、通常の Region 境界の規則は変えない。親 Region を暗黙に Back しない。

Single / Multiple は同時に保持できる実体数の契約であり、履歴数や復帰方法ではない。
Single 実体で章 1 → 章 2 → Back と進む場合、章 1 の項目を残して章 2 を追加し、戻るときには同じ実体を章 1 の Route と保存状態で再準備する。
履歴から章 2 を除去しても、章 1 が使用する実体や DI スコープを破棄しない。

## 復帰時の制御

表示準備と入場演出を別々に選択する。実体の再生成は、どちらの設定にも含めない。

### 表示準備

ScreenPreparationMode は次の二値とする。

| 値 | 契約 |
| --- | --- |
| WhenRequired | 同じ項目の準備済み実体があれば準備を維持する。実体がない、別項目を表示中、準備が失効した場合には PrepareAsync が必要 |
| Always | 保持中でも今回の復帰に対して PrepareAsync を実行する |

WhenRequired は必要な復元を省略する指定ではない。
Always は Scene の再ロード、InitializeAsync の再実行、DI スコープの再作成を意味しない。
本書で「再入場」と呼ぶ処理は、同じ履歴項目について表示を再準備し、必要な入場演出を経て活動を開始することである。
購読の再接続だけでよい場合は、準備を維持して活動を再開する。

### 入場演出

ScreenEnterAnimationMode は次の四値とする。

| 値 | 契約 |
| --- | --- |
| WhenChanged | 前面・背面・非表示の状態が変わる場合に、画面の復帰演出を行う |
| WhenShown | 戻り先が非表示から表示になる場合に、登録済みの画面入場演出を行う |
| Always | 下層で表示を維持していた場合も、画面入場演出を行う |
| Skip | 画面入場演出を待たず、Animator の即時状態反映で入場済みの状態にする |

Animator の未登録は演出なしとする。入場演出を省略しても、PrepareAsync や ActivateAsync は省略しない。
この設定は戻り先の IScreenAnimator にのみ作用する。退出画面の演出、遷移全体のフェード、ブロッカーの規則は変えない。
全体のフェード等は既存の NavigationOptions.Transition と Region の遷移設定で選ぶ。

### 設定する場所

画面固有の標準は ScreenDefinition.HistoryReturn に置く。戻り先の振る舞いを、すべての呼出し元へ重複して指定しない。
ScreenHistoryReturnOptions は Preparation と EnterAnimation を持つ不変の設定で、既定値は WhenRequired / WhenChanged とする。
表示と演出の接続は[画面の表示構成と演出](../../30_technical/features/navigathena_screen_presentation.md)を正本とする。

```csharp
// CreateLeaderboardAsync はゲーム側の構築処理。
var leaderboard = new ScreenDefinition<LeaderboardRoute>(CreateLeaderboardAsync)
{
    HistoryReturn = new ScreenHistoryReturnOptions
    {
        Preparation = ScreenPreparationMode.Always,
        EnterAnimation = ScreenEnterAnimationMode.WhenShown
    }
};
```

今回の戻る操作だけ異なる意図がある場合は BackOptions で上書きする。
BackOptions は nullable の Preparation / EnterAnimation と、共通操作と同じ意味の Transition / Progress を持つ。
省略した値は戻り先の定義を使う。履歴操作の入力型に結果値を設けない。

```csharp
// 活動中のイベントから呼ぶ公開署名。
NavigationOperation Back(BackOptions? options = null);

// 通常は戻り先の設定に従う。
activity.Navigation.Back();

// 今回だけ再準備し、画面本体の入場演出は省く。
activity.Navigation.Back(new BackOptions
{
    Preparation = ScreenPreparationMode.Always,
    EnterAnimation = ScreenEnterAnimationMode.Skip
});
```

IScreenNavigation、Work の Navigation、Host.Client の明示対象付き Back に同じ設定を適用する。
BackAsync はこの操作の完了を待つ。終了する画面自身の Work から、自身の終了を含む完了を待ってはならない。
優先順位は今回の BackOptions、戻り先の ScreenDefinition.HistoryReturn、ライブラリ既定値とする。
複数 Region や子画面が再活動する場合、要求の上書きは直接の戻り先だけに適用し、子はそれぞれの定義に従う。
Call の正常終了で戻る場合も同じ復帰計画を使うが、結果受信 API から復帰を指示させず、戻り先の定義に従う。
障害からの復旧には別の復旧計画を使い、BackOptions で安全上必要な処理を省略できない。

## ライフサイクルへ渡す情報

一実体一つの IScreenLifecycleHandler を維持する。復帰専用の独立した参加者は追加しない。
InitializeAsync / PrepareAsync / ActivateAsync / DeactivateAsync / TerminateAsync の五つの責務を維持する。

ScreenPreparationContext.Reason は ScreenPreparationReason 型である。

| 理由 | 意味 |
| --- | --- |
| NewEntry | 新しい履歴項目のための準備。実体を再利用する場合も含む |
| HistoryRestoration | 既存項目の表示を復元する。新しい実体の生成と、別項目に使用中の実体の再利用を含む |
| Reentry | 同じ項目の準備済み実体を、Always の指定で再準備する |
| Recovery | 失敗または物理消失に対する復旧処理 |

ScreenActivityContext は ScreenActivationReason 型の Reason と、nullable の PreparationReason を持つ。
PreparationReason は今回の活動開始に先立って行った準備を表し、準備を維持した場合は null とする。

| 活動開始理由 | 意味 |
| --- | --- |
| Entry | Push / Replace / Reset / 起動で新たに選ばれた項目の活動開始 |
| HistoryReturn | Back、または Call 終了に伴う、既存の戻り先項目の活動開始 |
| Resume | 親の活動や構成の変化などによる、履歴を戻さない同一項目の活動再開 |
| Recovery | 復旧処理による活動開始 |

戻り先の復元に伴って活動を再開する子にも HistoryReturn を伝える。
理由は Runtime の計画で確定する。画面側が直前の Route 型や、Call の有無から推測しない。
ここへ回答値、object 型の受信データ、ReturnPort は置かない。

IsFirstActivation は実体ではなく履歴項目に属する。最初の活動開始を含む遷移が成功した時点で消費する。
同じ項目の実体再生成・活動再開・再入場では false、新しい項目では true とする。
活動開始に一度も成功していない項目の復旧では true のままでよい。Reason と IsFirstActivation は異なる情報である。

## 利用者が書く復元処理

```csharp
// ゲーム側 Presenter の実装例。
public async ValueTask PrepareAsync(
    CampaignMapRoute route,
    ScreenPreparationContext preparation,
    CancellationToken token)
{
    var chapter = await campaign.LoadChapterAsync(route.ChapterId, token);
    view.Render(chapter);

    if (preparation.SavedState is CampaignMapState state)
    {
        view.RestoreSelection(state.SelectedStageId);
    }
}

public ValueTask ActivateAsync(
    CampaignMapRoute route,
    ScreenActivityContext activity)
{
    commands = view.SubscribeCommands(activity.Navigation);
    return default;
}
```

CampaignMapState、campaign、view、SubscribeCommands はゲーム側の型・処理である。
表示データと保存状態の適用は PrepareAsync、購読の接続は ActivateAsync、購読の破棄は DeactivateAsync に置く。
履歴 ID、元の Route、保存状態を Runtime が渡すので、通常の画面が Reason ごとの分岐を必ず書く必要はない。
復帰かどうかで活動処理を変える画面だけ、activity.Reason を参照する。

保存値の取得は同じハンドラーの IScreenStateCapture.CaptureState が担当する。
保持中の実体を Always で再準備する場合も、停止後の最新状態を保存して渡す。古い履歴スナップショットで現在の選択を上書きしない。
保存値がない場合は null を渡し、Route から表示を準備する。保存値は画面の不変データであり、DI スコープ、View、実行中 Task を含めない。
最新データの再取得と、選択位置などの復元は両立する。保存値をどう適用するかは画面ロジックが決める。

## 実行規則

| 戻り先の状態 | Initialize | Prepare | Activate |
| --- | --- | --- | --- |
| 同じ項目の実体・準備を保持、WhenRequired | 呼ばない | 呼ばない | 停止していた活動を新世代で再開 |
| 同じ項目の実体・準備を保持、Always | 呼ばない | 最新保存値で Reentry | 新世代で開始 |
| Single 実体が別項目を表示中 | 呼ばない | 戻り先の Route・保存値で HistoryRestoration | 新世代で開始 |
| 戻り先の実体が解放済み | 新実体で一回 | 戻り先の Route・保存値で HistoryRestoration | 新世代で開始 |

戻り先が継続して活動中で、再準備や演出による停止も不要なら、活動を重複して開始しない。
履歴・子構成の変更通知は既存の INavigationChangeHandler で受けられる。これは回答通知ではない。
Always や、表示中の画面への入場演出を要求する場合には、活動を閉じてから実行する。

実行順序は、受付・型・世代・使用制約の検証 → 入力と必要な活動の停止 → 保存 → 必要な退場・切替準備 → 構築または再準備 → 履歴確定 → 入場・演出の整合 → 活動開始 → 入力許可とする。
準備が旧表示を変更する前に、その表示を使う演出やブロッカーの使用を終える。表示を維持したまま書き換えてよいとは仮定しない。
同一実体の旧表示と新表示を、同時に必要とする演出は事前に拒否する。

親実体を別項目に再利用するときは旧項目の子実体を終了し、子履歴・保存値を親の項目に残す。
Back では親を準備してから、その項目の子履歴を復元する。新しい項目の初期子構成で上書きしない。

同じ項目の再準備では、その項目に属する Work や待機中の Call を取り消さない。
Work が参照する旧準備世代の資源は、その Work の終了まで保持する。新しい準備には別の資源所有範囲を用意する。
Runtime が利用者コード中の参照を検出できるとは仮定しない。Work の開始時の準備世代と、その Work の存続中に採用された準備世代を保守的に保持し、最後の使用者が終了してから解放する。
保持はオブジェクトの寿命を保証するもので、View の内容が不変である保証ではない。Work は保持中の View を Host の実行 context 上で操作できる。非活動化と資源解放を同一視せず、古い Activity の入力・遷移権限は再利用しない。再準備と並行するゲーム処理の表示内容の調停はゲーム側の責務とする。
この保持をせずに PrepareAsync の再実行だけを追加してはならない。
別項目への実体再利用は、存続する旧項目の Work が実体を使用中なら事前に拒否する。

失敗時の入力閉鎖、旧状態による復旧、確定後の障害報告は共通 Runtime が担当する。
利用者の非同期 callback の直前・正常完了後のキャンセル確認も Runtime が行う。停止・解放は取消で省略しない。
実体再生成を明示要求する API は、この復帰設定へ混ぜず、別の構築・終了契約として設計する。

## 実装と受け入れ条件

BackOptions、HistoryReturn、準備・活動理由、履歴項目ごとの IsFirstActivation を共通 Runtime が管理する。
通常履歴の復元は ScreenHistoryContractTests、再準備・入場演出と回答待機の組合せは ScreenCallContractTests、Unity と三方式の構築は ScreenScopeUnityTests で検証する。

- 同じ実体で章 1 → 章 2 → Back を行い、項目 ID・Route・選択状態が章 1 に戻る。
- 保持モーダルの Back は、WhenRequired なら Prepare を増やさず、購読だけを一回再接続する。
- Always は最新の保存値で Prepare を実行するが、Scene、View、Presenter、DI スコープを再生成しない。
- 解放済み実体は WhenRequired でも必ず再生成・復元し、初回の訪問扱いにしない。
- 表示維持、非表示、単一実体再利用に対し、入場演出の三方式が独立して動く。
- 子履歴を復元し、無関係な子項目の初期状態を混入しない。
- 同じ項目の再準備と Work の共存で、旧資源の先行破棄・待機の循環・同じ処理の再起動が起きない。
- 初期化、再準備、保存、入場、活動開始の失敗では、確定済み履歴と復旧状態を正しく報告する。
- DI なし・Microsoft DI・VContainer、および Unity の実物で同じ履歴・活動・所有の振る舞いを確認する。
