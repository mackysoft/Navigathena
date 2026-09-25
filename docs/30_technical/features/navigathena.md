---
authority: canon
---

# Navigathena

Navigathena は、遷移要求に応じて履歴と画面構成を更新し、画面の構築、活動、表示、入力、演出、終了を調停する。Unity の Scene 切替そのものを画面管理の意味にはしない。

公開 API と依存関係は[クラス設計](navigathena_class_design.md)、Unity での登録と起動は[使い方](navigathena_usage.md)を参照する。

[履歴への復帰・再入場](../../50_tickets/technical/navigathena_history_reentry.md)と[画面呼び出し](../../50_tickets/technical/navigathena_screen_calls.md)は別契約である。Back は元の Route・保存状態への復帰、InvokeAsync は画面の回答または終了の待機を担当する。フローの順序や条件はゲームが記述し、共通 Runtime が活動停止・復帰・処理の終了待ち・資源終了を調停する。

## 責務の境界

| 担当 | 所有する判断と処理 |
| --- | --- |
| ゲームの構成・進行 | いつ何を開くか、必要な Scene・Prefab・既存 View、ゲームサービス、永続化 |
| 共通 Host / Runtime | 要求の受付と予約、履歴、画面実体、活動期間、資源所有、演出の選択と実行、ブロッカー、復旧、終了観測 |
|ライフサイクル実装| 取得済み View とゲームサービスを使う画面ロジック。初期化、活動開始・停止、自分の処理の終了 |
| Unity アダプター | Scene 内 Component の解決、GameObject の生成・破棄、ネイティブオブジェクトの消失検出 |
| UI アダプター | 共通の表示順・出力許可・物理入力許可を UIDocument または Canvas に反映 |
| Addressables アダプター | Scene / Prefab アセットの読み込みと Addressables ハンドルの解放 |

別エンジンへ移植するときに、Host、画面カタログ、活動、遷移演出の実行、所有管理を再実装しない。UnityNavigationHost や Unity 専用のカタログは公開しない。

常駐 Scene、DontDestroyOnLoad、起動専用 Scene のどれを使用するかはゲームの構成である。ライブラリはそのいずれも起動条件にしない。

NavigationHost の寿命は外部所有者が決める。アプリケーション全体で維持しても、特定の機能の終了時に ShutdownAsync してもよい。Root Region の Reset は論理的な画面構成の変更であり、Root Scene、Host、共通サービスを終了しない。Host 自体を作り直す場合だけ、外部所有者が旧 Host の終了を待って新しい Host を生成・起動する。終了した Host は再起動しない。

## 履歴と実体

Route は行き先と不変な引数を表す。同じ Route 型の Push ごとに独立した NavigationEntry ができる。View、Scene、ライフサイクル実装、delegate を Route に保存しない。

Region は独立した履歴と操作先を表す。画面内のすべての View に Region を作る必要はない。一つの系列へ追加して戻るだけなら同じ Region を使う。親の履歴を残して子の系列だけを Replace / Reset する場合に子 Region を使う。

| 操作・規則 | 意味 |
| --- | --- |
| Push | 履歴に新しい Entry を追加する |
| Replace | 現在 Entry とその子構成を除去し、新しい Entry に置き換える |
| ReplaceFrom | 指定 Entry を含む末尾の範囲とその子構成を除去し、新しい Entry に置き換える |
| Back | 現在 Entry を除去し、保持していた下位の構成へ戻る |
| Reset | 対象 Region の履歴全体を指定した構成に置き換える |
| Reload | 現在 Entry・Route・子履歴を維持し、実体と画面スコープを再構築する |
| Clear | Optional な子 Region を空にする |
| Exclusive | 最後の Entry とその子構成を提示する。それ以外の論理履歴は保持する |
| Layered | Entry とその子構成を定義順・履歴順に重ねる |
| Required | owner が存在する間、一件以上の Entry が必要 |
| Optional | owner が存在していても空にできる |

子 Region の論理寿命は owner Entry に従う。一方、実体の資源依存は登録・借用に従う。カタログに独立して登録した子画面を、論理上の親子関係だけで親の資源へ依存させない。親の準備で登録した子 factory は、その親の取得済み依存を使用できるため親実体の寿命に結び付く。

NavigationState の Entry と物理実現の PresentationId を区別する。実体を解放しても Entry は履歴へ残せる。再生成では同じ Entry に新しい PresentationId を割り当てる。

ScreenDefinition の InstancePolicy は標準が Single。同じ Host 内の同じ定義に生存する実体を一つに制限する。Multiple は独立した取得・解放を構築処理が保証する定義である。複数の Route や Region へ登録する同じ構築元は、同じ定義オブジェクトを共有する。

履歴項目は ID・Route・ScreenDefinitionId・保存状態を保持し、実体が現在表示する項目とは分離する。通常の Single で章1から章2へ Push した場合、章1の保存状態を残して同じ実体へ章2を準備する。Back では章1の Route と保存状態を渡す。Scene・View・DI スコープは増やさない。Single は常駐指定ではなく、保持方針による解放後は新しい実体を生成できる。

NavigationOptions.RecreateInstance は、この要求の目的地と初期子画面の既存実体を流用しない指定である。Single なら旧実体の終了後に構築し、新しい訪問へ旧保存状態や子履歴を持ち込まない。借用する外部 View や共通サービス自体は作り直さない。新しいプレイは Replace / Reset、同じ訪問の再構築は Reload として区別する。

ReplaceFrom の HistoryTarget は対象 Region の Current、実型が一件だけ一致する Unique<TRoute>()、正確な Entry(entryId) を指定する。親・子 Region を暗黙に検索しない。受付時に対象を固定し、影響範囲の履歴変更を検出した場合は競合として拒否する。指定位置を含む末尾と子構成を一度に置き換え、途中の画面を活動再開させない。除去された Call は取消になり、その呼び出し元が残る場合だけ復帰する。

同じ単一実体に二項目の同時表示・活動を要求する構成と、単一実体の前後を同時に使う演出は拒否する。暗黙の複製や演出変更はしない。親実体を再利用するときは旧項目の子実体を終了し、子の論理履歴と保存状態は戻るまで保持する。

## 表示・入力・保持

Screen は、表示・活動・入退場・寿命を独立して管理する単位である。ヘッダーやボタンなど、画面と一緒に出入りする部品に個別の Route を作らない。Scene の所有範囲と表示制御の範囲も区別する。同じ Scene にあるマップ描画と HUD を、必ず一緒に非表示にするわけではない。

LowerPresentationPolicy は、同じ Region の下位履歴項目と、それらに属する子画面へ与える効果である。親画面、別の Region、自分自身の子画面へは逆流・横断しない。

| 設定 | 下層出力 | 下層入力 | 下層実体 |
| --- | --- | --- | --- |
| Preserve | 維持 | 通過可能 | 保持 |
| BlockInput | 維持 | 遮断 | 保持 |
| HideAndRetain | 非表示 | 遮断 | 保持 |
| HideAndRelease | 非表示 | 遮断 | 解放し、履歴は保持 |

効果は Region 内で前面から下位へ累積し、対象項目の子 Region へ継承する。子 Region 内の設定はその Region の下位だけへ累積する。Release は Hide と Block を伴う。親項目が非表示ならその子画面も非表示になり、親項目が上位画面によって入力を遮断された場合はその子画面も活動・入力を停止する。子だけを隠したり解放したりしても親や隣の Region は変更しない。

通常 HUD → メニュー → 編集 HUD を同じ Layered Region に積む場合、メニューは BlockInput、編集 HUD は HideAndRetain とする。編集開始の Push で下位 UI を隠して保持し、Back でメニューを再表示・再活動させる。通常 HUD は見えるが、メニューによる入力遮断は続く。退避対象の列挙や手動の再表示は行わない。複数の子 Region を一緒に退避する場合は、それらを所有する項目が置かれた上位 Region へ Push する。退避のためだけに履歴を持たない空の Screen や Region を追加しない。

前面は Host 全体で一つではなく、所属 Region と親項目の位置から求める。EffectiveComposition.InputBoundaries は、各 Region の有効な入力遮断元を RegionId と OwnerEntryId で示す。物理入力を開くには出力許可と入力資格があり、必要な画面活動と遷移が成立している必要がある。

活動停止と非表示と破棄は別の操作である。下層を表示したまま活動を停止できる。画面のゲーム入力や購読はライフサイクル実装が活動ライフサイクルで停止する。UI アダプターによるイベント遮断だけにゲームロジックの停止を頼らない。

IViewAdapter はネイティブ View の表示制御への接続であり、View の生成や所有権移譲、Transform の親変更は行わない。同じ物理 View の重複登録は拒否する。一つの Host で重なりを比較する View は、同じ描画順比較領域を使う。

## 構築と活動

機能の登録は ScreenCatalog.Build でまとめる。RegisterScreens callback の中で、exact Route 型から ScreenDefinition<TRoute> への対応を登録する。取得元を Route の値で選ぶ場合も、安定した定義を同期的に選択してから実体を取得する。カタログ構築時に構築処理を実行しない。登録 callback 終了後の builder 操作と重複登録は拒否する。

構築処理は ScreenCreationContext<TRoute>.Resources で資源を取得・借用し、IViewAdapter と必要な演出を登録する。取得をすべて await して、一つの IScreenLifecycleHandler<TRoute> を返す。準備 context を初期化や活動へ持ち越さない。

ライフサイクル実装の基本サイクルは次のとおり。

1. 構築処理が取得済み依存から一つのライフサイクル実装を生成・解決して返す。
2. InitializeAsync を実体ごとに一回実行する。
3. PrepareAsync に今回の Route と保存状態を渡し、表示前の完了を待つ。
4. ActivateAsync に同じ Route と新しい ScreenActivityContext を渡す。
5. 活動停止時に活動 token と操作窓口を失効させ、DeactivateAsync の完了を待つ。
6. 別の履歴項目を表示する場合は再準備する。同じ項目の活動再開だけなら準備を繰り返さない。
7. 実体終了時は活動停止と TerminateAsync を待ち、管理接続、所有オブジェクト、入力準備資源、構築時の取得資源を解放する。

同じ実体の再開では InitializeAsync を繰り返さない。過去の活動 context に属する遅延 callback は、再開後も新しい画面を操作できない。

### 非同期拡張処理のキャンセル契約

キャンセル済みの処理を開始しないための判定は Runtime が担当する。画面・ブロッカー・遷移演出の構築処理、画面の InitializeAsync・PrepareAsync・ActivateAsync、ブロッカーの PrepareAsync、資源の AcquireAsync、画面とブロッカーの非同期アニメーション、遷移演出の BeginAsync・PrepareSwitchAsync・AfterCommitAsync が対象となる。

- Runtime は実装を呼び出す直前に、渡す token を確認する。ActivateAsync では ScreenActivityContext.CancellationToken を確認する。キャンセル済みなら呼び出さない。
- 呼び出した処理は完了まで待ち、正常に戻った後にも token を確認する。構築結果や取得資源の所有を失わず、キャンセルされていれば次の処理へ進めない。実装が例外を送出した場合は、その失敗を保持する。
- 実装側に、入口で同じ ThrowIfCancellationRequested を繰り返す義務はない。非同期の依存処理へ token を渡し、長時間の計算などでは処理途中のキャンセルに対応する。
- 呼び出し直前の確認と開始は不可分ではない。確認直後や実行中のキャンセルは協調的な停止の対象であり、Runtime が実行中の処理を強制中断する保証はない。キャンセルされた場合も実行中の処理の終了を待ち、使用中の資源を先に解放しない。
- DeactivateAsync・TerminateAsync・DisposeAsync と演出の SettleAsync は停止・整合・解放の処理であり、キャンセルで省略しない。SettleAsync には CancellationToken.None を渡す。取得開始を省略した IResourceAcquisition も、登録済みなら DisposeAsync の対象になる。

この契約は操作の取消可能範囲を広げない。確定後の演出には呼出し元の操作取消ではなく Runtime の終了に対応する token を使用し、終了による中断が起きても確定済みの履歴は巻き戻さない。ブロッカーの旧アニメーションの取消は表示切替に伴う正常な停止として扱う。

## 資源所有と復元

AcquireAsync は取得オブジェクトを登録してから取得を開始する。取得途中の失敗も、そのオブジェクトの DisposeAsync の対象になる。取得オブジェクトの constructor では資源を取得しない。終了は取得と並行実行せず、逆順で行う。後に取得した依存先の終了が失敗した場合、先に取得した資源を保持する。

外部所有の資源は ResourceLifetime.Reference と BorrowAsync で渡す。外部所有者は EndAsync を待ってから自分の資源を破棄する。EndAsync は新しい借用を閉じ、使用者を止めるが、外部資源そのものを破棄しない。同時に呼ばれた EndAsync は同じ試行へ合流する。終了失敗を再呼出しで自動的に再試行しない。

復元が必要なライフサイクル実装は任意の IScreenStateCapture を実装する。CaptureState の不変値を Entry に保持し、再準備時の ScreenPreparationContext.SavedState へ渡す。選択位置やスクロール位置と、ゲーム進行の永続状態は分ける。予期しない物理消失の直前値まで保存できるとは保証しない。

## DI 接続と破棄

ライフサイクルの実装とオブジェクトの所有者は別である。DI なしでは Resources.CreateOwned 等で所有し、構築処理から返す。戻り値は破棄所有を増やさない。画面実体の入口は一つであり、保存状態もそのハンドラーの IScreenStateCapture 実装で扱う。内部のサービスを複数用意しても、Runtime の独立したライフサイクル参加者にはしない。

Microsoft DI は実体ごとに独立した IServiceCollection、Provider、実行スコープを作る。ImportService は既存インスタンスを借用する明示的な入口であり、親コンテナの登録をコピーしない。Provider と実行スコープの非同期破棄を待つ。

VContainer は指定された IObjectResolver から子スコープを作り、標準の IInstaller または登録 callback を使用する。非同期停止は共通ライフサイクルで完了させ、コンテナには標準の同期破棄を行わせる。ゲームの Inspector 参照はゲーム側の Installer が持ち、スコープの寿命は持たない。

Route のコンストラクター注入、DI 登録、現在 Route の注入用ホルダーは使用しない。画面実体を再利用するときにスコープを作り直さず、履歴項目ごとの DI コンテナを追加しない。共通コンテナは Host または管理された借用の終了を待ってからゲーム側が破棄する。

## 遷移演出と資源方針

演出は操作ごとに解決する。通常の設定を RegionNavigationOptions に置き、今回だけ異なる意図があれば NavigationOptions.Transition で上書きする。NavigationTransition.None は明示的な演出なしであり、設定省略とは異なる。

優先順位は、要求の指定、操作と両端 Route の一致、操作と片端 Route の一致、操作別既定、Region 既定、演出なし。遷移元だけと遷移先だけのルールは同じ優先度とし、異なる演出が競合すれば拒否する。

演出の実体は操作の factory、遷移元の画面、遷移先の画面から取得できる。取得方式で履歴、取消、活動、結果の意味を変えない。Scene 内に置かれた View を使用する演出は、その Scene を取得した後で生成する。

通常の IncomingFirst は、離れる画面の入力と活動を止め、演出前段階、新画面の準備、構成確定と変更通知、入退場と演出後段階、演出の整合と終了、活動開始、入力開放の順で進める。異なる実体の入退場と演出後段階は並行して待てる。同じ実体を再利用するときは、旧表示の退場を完了してから新 Route を準備し、入場を実行する。PrepareSwitchAsync の完了後、演出は旧項目の表示データを使用しない。

Commit observer は確定順に、確定区間の外で呼ぶ。演出や旧資源の終了は待たない。保持ライフサイクル実装が INavigationChangeHandler を実装している場合は、自己・子 Region の変更を入力再開と画面入退場の前に同期通知する。

OutgoingFirst は Region の資源方針として指定する。復元値を取得し、旧画面を解放してから新画面を取得する。旧画面と新画面の同時保持を必要とする演出、および解放対象の遷移元が所有する演出とは併用しない。新画面の取得失敗では旧構成の再生成を試みる。

## ブロッカー

ブロッカーは入力を遮る非フル画面の背面表現であり、Route や履歴を持たない。構築処理を BlockerDefinition として一つ定義し、NavigationHostOptions.DefaultBlocker に設定する。RegisterBlocker で画面固有の定義も選べる。同じ Host 内の同じ定義は、一つの実体を生成して再利用する。Region や Route が違うことを理由に複製しない。

ブロッカーの見た目、操作説明、ボタン構成はゲームが実装する。Runtime が準備・表示切替・操作対象・保持・終了を管理し、画面ライフサイクル実装に寿命管理を委譲しない。

入力を受け付けられる各 Region の遮断元から、下層出力を維持する画面のブロッカーを選ぶ。同じ Region の上位画面が下層を隠す場合や、上位 Region から親項目が隠される・入力を遮断される場合は、該当する下位のブロッカーを表示しない。別の Region のブロッカーには影響しない。ブロッカーが非表示でも下層画面の入力資格は戻さない。

BlockerPreparationContext は実体の資源取得と表示接続を担当し、特定 Region を構築先としない。対象画面・Region・Route は接続ごとの BlockerScreenContext で受け取る。Region は矩形や Canvas ではなく、Runtime は見た目を自動的にクリップしない。ゲームが View の描画・ヒット領域を設定する。

既定設定とカタログに登録した定義の実体は Host が所有する。接続解除や Region 除去では破棄せず、Host 終了で解放する。親画面の構築中に登録した子画面用の定義は、親画面の実体が所有し、その資源を解放する前に終了する。同じ定義を異なる親実体の所有物として同時に使用する登録は拒否する。準備失敗で初めて取得した実体は、履歴を確定せずに終了する。

一つの定義に独立した複数の画面への同時接続を要求した場合、構成不備として確定前に拒否する。既に登録済みの構成で判断できる場合は、現在画面の活動停止より前に拒否する。独立した表示・入力領域で同時使用する場合だけ、それぞれの View を取得する別定義を明示する。複製や接続対象の間引きを自動で行わない。

接続を変更する間は定義を予約する。同じ共有ブロッカーを操作する遷移は、Region が異なっても競合する。別定義の接続は独立して進められる。全体演出を選んだ場合は Host の予約を使用する。

共通 A から共通 A は再生成せず、A から別の B は同期的に切り替える。クロスフェードや入退場の完了待ちは挟まない。なしとの間では任意の IBlockerAnimator が装飾を演出できる。装飾の後始末は Runtime が追跡するが、通常の遷移結果の完了条件には加えない。

BlockerScreenContext はブロッカーを使う画面と所属 Region を指す。遮断される下層画面ではない。その接続期間に限定した Navigation を渡し、古い callback を別の画面へ付け替えない。

### 描画順

共通 Runtime は画面、接続されたブロッカー、退場中の画面、実行中の遷移演出を一つの順序へまとめる。ブロッカーを対象画面の直下、遷移全体の演出を画面群の上に置く。画面内に複数 View がある場合は取得時のネイティブ順序を保ち、同値の場合は登録順に並べる。固定幅の枠は使わず、実際の View 数から順序を割り当てる。

並行する遷移が確定しても、まだ退場中の View や実行中のオーバーレイを順序計算から外さない。アダプターは整数順序を Canvas や UIDocument へ反映し、設定した比較領域や表現可能な範囲を検証する。履歴、ブロッカーの所有、順序計算を Unity 側へ複製しない。

## 操作結果、復旧、終了

通常の PushAsync・ReplaceAsync・ReplaceFromAsync・ResetAsync・BackAsync・ReloadAsync・StartAsync は Task を返し、正常完了は要求した遷移の成立を保証する。cancellationToken は操作の取消を要求し、取消後の停止・復旧も完了まで待つ。明示的な診断・独立待機が必要な操作は NavigationOperation を返す入口を使う。要求時点で履歴と必要な共有表示の予約を行い、競合を待ち行列へ隠さない。WaitAsync の token は結果待機だけを取り消す。操作そのものの取消は TryRequestCancellation で要求する。確定後、先行解放後、または単一実体の書き換え開始後は受け付けない。書き換え途中の失敗では保存した Route・状態による再準備を試みる。復旧できなければ入力と活動を閉じて障害を報告し、古い履歴が残っていることを表示の安全性と同一視しない。

NavigationResult は明示的な操作ハンドルと観測通知が扱う診断情報である。通常の非同期 API では要求拒否・競合も例外となり、成功処理の前提として結果の分岐を書かせない。実行失敗は元の原因を InnerException に持つ NavigationException、成立した取消は OperationCanceledException とする。設定不備は実行前に例外で通知する。NavigationException の DestinationCommitted は履歴確定を表し、FinalSnapshot と PresentationStatus は失敗時の履歴・表示状態を表す。表示障害が起きても確定済み履歴を巻き戻さない。

物理消失では活動窓口を失効させ、生存している入力境界を閉じてから、実際に依存する実体を Lost として報告する。復旧は該当 incident と生成世代に限定する。終了中または終了失敗した旧実体を理由に、無関係な新実体の資源まで保持しない。

安全に切り離した旧実体の終了は Host が追跡し、通常の遷移結果を待たせない。ただし明示した履歴境界からの ReplaceFrom、RecreateInstance を指定した遷移、Reload は、除去した実体の必須終了まで待つ。Terminations.Current と WaitForChangeAsync で未完了・失敗した所有を観測できる。返却済み結果を後から書き換えない。

ShutdownAsync は受付を閉じ、受理済み処理と所有資源の終了を待つ ValueTask である。全終了した場合だけ正常完了し、終了失敗は元の原因を保持した AggregateException で通知する。失敗した所有と依存資源を保持し、Terminations から詳細を確認できる。再度の ShutdownAsync と DisposeAsync は同じ試行と例外へ合流し、破棄を再実行しない。
