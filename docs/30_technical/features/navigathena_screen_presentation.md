---
authority: canon
---

# Navigathena：画面の表示構成と演出の接続境界

## 責務

画面の表示構成を一つの管理対象として接続し、通常の画面構築から個別の表示アダプター・演出の登録手順を取り除く。
ゲームは画面の取得元とロジックの組み立てを指定する。
共通 Runtime は画面ごとの表示状態の変化、活動、入力、演出待機、所有、復旧を調停する。
Unity アダプターは Inspector の構成と取得済み Component を共通契約へ変換する。

共通ポップアップの Route、Presenter、View、Prefab はゲーム側の共通 UI として定義する。
ゲームの構成処理が対象 Region の Route と ScreenDefinition を登録し、呼出し元の画面ごとに登録を繰り返さない。
共有する定義と実体数は別であり、同じポップアップを重ねて開く場合は独立した生成・解放を行う Multiple の構築定義を使用する。

### Inspector で設定する画面構成

Unity アダプターの ScreenPresentation コンポーネントを使用する。
このコンポーネントは、一つの管理対象画面について、表示制御に使用するアダプター群と、省略可能な画面演出コンポーネントへの参照を持つ。
Scene 全体、任意の View、ライフサイクルハンドラーを画面と同一視しない。
複数の表示部品を一画面として扱うことも、同じ Scene に独立した複数の画面を配置することも許容する。

| Inspector の設定先 | 設定するもの |
| --- | --- |
| ScreenPresentation | この画面を構成する表示アダプターと演出担当への参照 |
| UI アダプター | Canvas / UIDocument などの具体的な表示・入力境界 |
| 標準の Animator 接続コンポーネント、または独自演出 MonoBehaviour | 対象、時間、各場面の演出、完了条件 |
| ゲーム側の View | 文言、ボタンなど、画面ロジックが操作する部品 |
| ゲーム側の構成・Installer | Prefab / Scene / 既存画面の取得元、サービスと Presenter の登録 |

ゲーム側の View に NavigationView / ScreenAnimator という参照プロパティを要求しない。
ScreenPresentation はライフサイクルハンドラーでも、Host でも、資源の所有者でもない。
Awake / OnEnable から Host を検索して自己登録しない。
参照の未設定、契約を実装していないコンポーネント、管理対象の重複は Editor 検証と実行時検証で構成エラーにする。
Editor 検証だけを安全性の根拠にしない。

通常の Unity Animator を使用するための接続コンポーネントはライブラリ側で提供する。
ゲームごとに再生と完了待ちのラッパーを実装させない。
独自の演出方式が必要な場合だけ、別の演出コンポーネントへ差し替える。

表示・入力の管理対象と、演出が書き換える位置・透明度・拡大率を分離する。
例えば Canvas の出力許可と GraphicRaycaster の入力許可は表示アダプターが管理し、子の RectTransform / CanvasGroup の見た目は演出が管理する。
同じプロパティを表示アダプターと演出の双方から書き換えない。
演出コンポーネントが入力を開いたり、ブロッカーを取り除いたりしない。
画面自身の入力受付と、下層画面への入力遮断は別の制御とする。

### 通常の構築 API

Unity には、画面の取得と表示構成の接続を一体として実行する拡張を用意する。
汎用の Resources.InstantiatePrefabAsync / LoadSceneAsync / BorrowAsync は資源取得のまま維持し、画面登録の副作用を追加しない。

```csharp
// confirmationPrefab は Inspector で指定する
// ConfirmationPopupView 型の Prefab 参照。
var confirmation = new ScreenDefinition<ConfirmationRoute, bool>(
    async (creation, token) =>
    {
        var view = await creation.InstantiateScreenAsync(
            confirmationPrefab,
            token);

        return creation.Resources.CreateOwned(
            () => new ConfirmationPresenter(view));
    },
    instancePolicy: ScreenInstancePolicy.Multiple);
```

InstantiateScreenAsync は Unity アダプターの API であり、共通 Runtime に GameObject を持ち込まない。
直接指定する Prefab は画面ルート上の Component 型の参照を標準とし、戻り値の型を推論できるようにする。
参照した子 Component だけを複製して画面ルートを欠落させることがないよう、生成対象と ScreenPresentation の対応を検証する。
Addressables など、取得前には Component の参照がない入口では View 型と解決対象を明示する。
取得した対象の ScreenPresentation を解決し、検証・接続を済ませてから View を返す。
既定の解決対象は、指定した View と同じ GameObject に置いた ScreenPresentation とする。
子階層や Scene 全体の ScreenPresentation を無差別に登録しない。
複数の同型 View がある場合は明示的な選択を必要とし、検索結果の先頭を使用しない。

取得 API の正常完了は、取得実体の構成検証、構築中の画面への仮接続、解放責任の登録が成立したことを意味する。
履歴の追加、入場演出の完了、活動開始、入力許可を意味しない。
返す View は画面ロジック用の参照であり、利用者に別の管理ハンドルの所有・受け渡しを要求しない。
表示を持つ一画面の構築には、一つの主たる表示構成を接続する。
その構成は複数の表示部品を含められるが、独立した別画面を同じ構築 context に追加することは拒否する。
補助 Prefab は通常の資源取得を使用し、子画面は子 Region の別の構築として扱う。

Scene を読み込む画面にも、外部所有の配置済み画面を借用する場合にも、同じ接続処理を使用する。
LoadScreenAsync はロード後に View を解決して接続する。
同型 View が複数存在する Scene では、取得 Scene を受け取る選択関数を指定する。
BorrowScreenAsync は ResourceReference と返却時の ScreenAnimationState を受け取る。
複合取得で既に管理された View を接続する場合は ConnectScreen を使用する。
Scene 取得後にその中の対象を解決するため、起動時に未ロードの View 参照を要求しない。
複数の画面が同じ Scene の資源を使う場合は管理された借用でつなぎ、各画面が同じ Scene を所有・解放する形にしない。
複合的な取得や独自の選択処理でも、取得済みの対象を同じ接続処理へ渡せるようにする。

表示構成の接続自体は Route や回答型を使用しない。
共通の ScreenCreationContext に Route 非依存の構築能力を置き、型付き context はライフサイクル・DI の型照合を担当する。
これにより、Unity の取得拡張に無関係な Route の型引数を指定させず、通常画面と回答付き画面に同じ取得 API を使えるようにする。

### 共通契約と拡張契約

共通 Runtime はエンジン非依存の表示構成を受け取り、表示制御と演出を別の役割として管理する。
表示構成をまとめても、IViewAdapter にアニメーションや資源取得の責務を追加しない。
演出へ活動開始・履歴確定・破棄判断を委譲しない。

通常の構築 context に個別の SetAnimator / RegisterViewAdapter を公開しない。
アダプターは共通契約の ScreenPresentationBinding を、MackySoft.Navigathena.Integration の ConnectPresentation 拡張で渡す。
単に名称を変えたり、同じ登録手順をゲーム側の helper へ移したりする対応にはしない。
第三者の UI / エンジンアダプターも、非公開メンバーへのアクセスなしに同じ契約を実装できるようにする。
非 Unity 環境では、その環境の構成方法から同じ表示構成を接続し、MonoBehaviour や空のコンポーネントを要求しない。

アダプターからは、表示制御、演出、取得済み資源への使用関係をまとめた接続情報を渡す。
全体を検証してから一つの構築候補へ接続し、個別 setter によって半分だけ構成された画面を公開しない。
接続情報の対象は今回の実体とし、Prefab アセットや Route 型を実体の識別子にしない。
資源は取得前から既存の共通所有機構へ登録する。
接続情報へ渡す使用関係は、同じ資源の新しい破棄所有者を追加するものではない。

### 実行フロー

1. ゲームが Catalog と共通 Host を構成する。
2. Push / InvokeAsync などの要求から、Runtime が必要な画面と構成変更を計画する。
3. 必要な実体について構築処理を呼び、Unity アダプターが Prefab / Scene / 借用画面を取得する。
4. アダプターが指定された ScreenPresentation を検証し、取得実体の参照から共通の表示構成を接続する。
5. ゲーム側の構築処理が一つのライフサイクルハンドラーを返す。
6. 共通 Runtime が準備、表示状態の変更、演出待機、活動開始、入力許可を調停する。
7. 終了時は共通 Runtime が使用・停止・演出完了を待ち、管理接続と依存資源を解放する。

接続が失敗した場合、構築途中の資源を管理下で終了し、画面を公開しない。
画面を取得する前の資源所有登録、キャンセル判定、復旧の調停は共通 Runtime の仕組みを使用する。
Unity 側に別の遷移実行機構を作らない。

### 構築中の表示と Unity の初期化

画面を生成・ロードした直後から、公開前の出力と入力が漏れない構成を取得契約に含める。
通常の画面は管理用の表示・入力ゲートを閉じた初期構成とし、Editor と実行時の両方で検証する。
既存の起動オーバーレイのように表示済みの資源は、この新規画面の初期構成とは分ける。

Unity は Instantiate 時に階層内で有効なオブジェクトの Awake / OnEnable を実行する。
そのため、生成後の SetActive(false) で生成時の初期化を防いだとは扱わない。
これは [Object.Instantiate の契約](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.Instantiate.html)に基づく制約である。
Unity の初期化を画面の入場や活動開始とは同一視せず、購読などの画面活動は共通ライフサイクルから開始する。

演出の実行対象と完了を観測するコンポーネントは、停止完了まで実行可能に保つ。
GameObject の無効化はコンポーネントとコルーチンを停止するため、活動停止をそのまま画面ルートの無効化で実装しない。
詳細は [GameObject.SetActive](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GameObject.SetActive.html)を参照する。

### 表現の状態と変化

画面ごとの演出へ、変更前後の表示上の位置付けと、その変化の意味を渡す。
前面・背面・非表示という見た目の位置付けを、活動状態・入力許可・資源の保持とは分ける。
前面と背面の関係は対象 Region と親子構成から計算し、Host 全体で一つだけの前面を仮定しない。

LowerPresentationPolicy の範囲は同じ Region の下位履歴項目とその子画面である。編集 HUD を Push してメニューと通常 HUD を退避させる場合は、三つを同じ Layered Region に登録し、編集 HUD に HideAndRetain を設定する。各 Screen の演出担当が Cover / Reveal を実行し、呼出し元が別 Screen の表示部品を列挙して隠さない。

子 Region 内の編集は親や兄弟 Region を退避させない。複数の子 Region を一緒に覆う場合は、その所有項目のある上位 Region に新しい Screen を積む。親項目が隠れればその子画面も隠れる。Back は元の構成を再評価し、以前から入力を遮断されていた下位画面まで活動再開させない。
入力を止めただけの画面を自動的に背面と扱わない。

| 変化の意味 | 表現上の対応 | 注意点 |
| --- | --- | --- |
| 新規入場 | PushIn | 実体を再利用しても、新しい項目への入場を区別する |
| 保持したまま退避 | PushOut | 下層方針により、表示したまま背面になる場合と非表示になる場合がある |
| 保持項目への復帰 | PopIn | 表示を残した背面から前面へ戻る場合にも成立する |
| 項目からの退出 | PopOut | 実体の破棄や、保持項目の一時非表示と同一視しない |

履歴上の操作名だけで演出を決めず、各画面について必要な変化を計算する。
Replace / Reset / 子 Region の変更 / Call 終了も同じ計画で扱う。
背面のままの画面へ、同じ退避演出を重ねて要求しない。
表示中の前面から背面への変化を、OutputEnabled が変化しないことを理由に省略しない。

演出の省略・中断・復旧では、前面・背面・非表示それぞれの確定した見た目を即時適用する。
同一実体を別の履歴項目へ再利用するときは、旧表示の演出が終了してから内容を切り替える。
前後の演出を同じ実体へ同時実行しない。
ブロッカーと遷移全体の演出は別の担当として維持し、同じ View プロパティを競合して操作させない。

HistoryReturn.EnterAnimation の既定値は WhenChanged とし、前面・背面・非表示の変化に応じて演出する。
WhenShown は非表示からの復帰だけを演出したい場合に明示する。
Skip を指定した場合にも復帰後の見た目を即時適用する。
再準備の要否は従来どおり独立して設定し、復帰演出を行うことを PrepareAsync の再実行条件にしない。
Invoke の回答値を演出へ配送する契約にはしない。

### 演出の完了・停止・即時反映

共通の演出契約は、変化の意味と変更前後の見た目を受け取る有限な非同期再生と、再生を経由しない即時反映を持つ。
IScreenAnimator.PlayAsync は ScreenAnimation を受け取る。
Kind は Enter / Cover / Reveal / Exit であり、活動の開始・停止ではなく画面の入場・退避・復帰・退出を表す。
From / To は BeforeEnter / Foreground / Background / Hidden / AfterExit の表示状態を表す。
SetStateImmediately は演出なしで指定状態を反映する。
変更前後の情報を省き、Enter / Exit という名前だけで非表示と退避の違いを推測させない。
入場前と退出後の見た目も区別できるようにし、演出省略や再利用を逆再生で代用しない。

正常完了は、再生命令を発行した時点ではなく、今回の演出が完了し、要求された終端の見た目が成立した時点とする。
取消完了は、その再生に由来する更新と完了通知が停止し、後始末を終えた時点とする。
待機だけを解除し、元の演出が書き込みを続ける形を演出自身の取消とは扱わない。
開始前と正常完了後の取消判定は Runtime が行い、演出実装は開始後の処理へ token を伝播する。

即時反映は、呼び出しが戻った時点で指定した見た目が成立する契約とする。
古い演出の停止完了後に呼び、直後に古い更新や通知で上書きされないようにする。
同一実体の演出、即時反映、資源解放は並行実行しない。

一回の遷移内で異なる実体の演出を並行させる場合、一方の障害を検出したら他方にも停止を要求し、全処理の終了を観測してから復旧・解放へ進む。
停止を確認できない演出が使用する実体は解放せず、入力・活動を閉じた障害として報告する。
Presenter が行った任意のゲーム状態の変更まで巻き戻すとは約束しない。

### 標準の Animator 接続コンポーネント

Inspector で対象 Animator、対象レイヤー、四種類の演出と即時反映先の対応を設定する。
標準コンポーネントは AnimatorScreenAnimationDriver とする。
専用 Animator を使用し、完全なステートパスを設定する。
再生ステートには非ループの有限クリップを指定し、自動遷移や外部からの再生操作を行わない。
Animator の更新はこのコンポーネントが独占し、完了・失敗・取消時に停止する。
即時状態用ステートの先頭を同期評価し、更新を停止して見た目を保持する。
無期限の待機を防ぐため、正の有限値の timeoutSeconds を設定する。
対応する Controller の条件を明示し、任意の Controller で正常完了を判定できるとは約束しない。
再生ごとの識別を持ち、以前の再生の通知で新しい要求を完了させない。

normalizedTime はループ回数と現在のループの進行を表し、レイヤーの遷移中かどうかは IsInTransition が別に表す。
したがって、対象ステートへの到達や遷移状態を確認せず normalizedTime >= 1 だけで完了する実装にはしない。
この判断は [normalizedTime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimatorStateInfo-normalizedTime.html) と [IsInTransition](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator.IsInTransition.html) の仕様から導く。
OnStateExit は中断でも呼ばれるため、それ単独では正常完了の証拠にならない。
詳細は [OnStateExit](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/StateMachineBehaviour.OnStateExit.html)を参照する。

対象ステートとレイヤーの存在、有限な完了条件、許可された遷移、外部中断や対象消失時の失敗条件を検証する。
ループや更新停止などで正常完了を判定できない設定を、成功扱いや無期限の無言待機にしない。
再生・停止・即時反映の保証を、実際の Controller を使う Unity テストで確認する。

標準の UI 演出は非スケール時間を使用し、ゲームの一時停止でも完了できるようにする。
Unity の [AnimatorUpdateMode.UnscaledTime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimatorUpdateMode.UnscaledTime.html) は Time.timeScale に依存せず更新する。

### DI と寿命

DI の有無で、Inspector の設定や画面の取得・表示接続を変えない。
DI がある場合は、取得した View を画面のスコープへ登録し、一つの Presenter を通常の DI 解決で取得する。
Presenter とサービスの登録は既存の型付きライフサイクル契約を使用する。
Route は PrepareAsync / ActivateAsync の引数で渡す。

Prefab / Scene が所有する演出コンポーネントを、CreateOwned や DI の破棄所有へ重複登録しない。
接続は使用関係であり、コンポーネントの所有権を移さない。
コードや DI で生成した演出も拡張契約から接続できるが、その所有者は構築時の Resources または DI スコープのいずれか一つとする。
既存画面の借用では、外部所有者が借用終了を待ってから破棄できる関係を維持する。
常駐 Scene、DontDestroyOnLoad、特定の DI コンテナを前提にしない。

借用の終了では、元の所有者へ返す表示・入力ゲートの状態と、演出の返却時の見た目を明示する。
接続時に記録したゲートの状態と、借用契約で指定した返却時の見た目を使用し、借用物を Destroy / Unload しない。
Animator の内部履歴や Presenter のゲームデータまで、取得前へ完全復元する契約にはしない。

画面の演出、ブロッカー、遷移全体の演出はそれぞれ異なる使用期間を維持する。
起動オーバーレイを接続する場合も、画面用の接続を流用して現在の表示を一度消してはならない。
現在の表示を維持した借用と、未公開の新規表示の接続を区別する。

### 履歴・確定・競合制御との境界

表示の接続変更によって、通常の Route と回答付き Route<TResult> の兄弟型関係、型付き Invoke、通常の Back を変更しない。
ScreenId による無型の入口や、回答先なしで回答付き画面を Push する経路を追加しない。

構築・準備の完了、論理履歴の確定、演出の完了、活動開始、入力許可を一つの時点と扱わない。
通常の実行では、必要な入力・活動停止と切替前処理、取得・準備、短い論理確定、画面演出と切替後処理、活動開始、入力許可という共通の境界を維持する。
全演出が完了するまで論理確定を遅らせる方式へは、本案だけで変更しない。
単一実体の書き換えや旧資源の先行解放は、論理確定より前でも取消可能範囲を狭める。
確定前という条件だけで、元の状態へ安全に戻せるとは扱わない。

確定後の表示・演出失敗では、確定済みの履歴と復旧が必要な状態を例外で報告する。
既存の NavigationOperation.WaitAsync の token は待機の取消であり、演出自身の停止 token や Invoke の呼び出し取消とは区別する。
演出の停止保証を強化することで、ナビゲーション操作の取消可能範囲まで広げない。

競合する画面実体・履歴・共有表示を共通 Runtime が予約する。
Host 全体を演出の期間中つねに直列化せず、独立した Region の並行性を維持する。
共有の全画面演出や同一実体を使用する場合は、その干渉範囲に応じて排他制御する。

## 接続箇所と検証

共通の接続箇所は [ScreenPresentationBinding](../../../src/MackySoft.Navigathena/Views/ScreenPresentationBinding.cs) と [ManagedScreenCreationContext](../../../src/MackySoft.Navigathena/Runtime/Screens/ManagedScreenCreationContext.cs) にある。
演出判定は [PreparedScreenPublication](../../../src/MackySoft.Navigathena/Runtime/Execution/PreparedScreenPublication.cs)、Unity の物理操作は [CanvasViewAdapter](../../../src/MackySoft.Navigathena.Unity.UGUI/Runtime/CanvasViewAdapter.cs) にある。
DI は表示接続を再実装せず、通常のサービス登録と単一ライフサイクル入口の解決を担当する。

次の外部挙動を .NET と実際の Unity テストで確認する。

- 演出コンポーネントを Inspector で差し替えても、構築コードや Presenter の変更を必要としない。
- DI なし・Microsoft DI・VContainer で、同じ Prefab と演出を使用できる。
- Prefab、ロード先 Scene、外部所有の既存画面で、対象と所有者を誤らず同じ表示接続が成立する。
- 同じ Scene に複数画面が存在しても、要求した画面以外を登録・非表示・破棄しない。
- 通常の資源取得だけでは画面登録を行わない。
- A の上に B、さらに C を開いて戻る際、変化する画面にだけ退避・復帰演出が行われる。
- 下層を表示したままの退避と、非表示にする退避を区別し、復帰後の位置・透明度・入力が正しい。
- 演出なし・省略・失敗・取消・復旧でも、確定した状態の見た目と活動・入力が一致する。
- Single の再利用で、旧内容の演出中に新しい Route の表示内容を書き込まない。
- 接続の不備・重複では公開前に例外となり、部分取得を終了する。
- 取得 API が戻っても Presenter の構築・準備が失敗すれば履歴へ追加せず、部分取得と仮接続を終了する。
- 同じ Prefab の複数実体間、または再生世代間で、演出の状態・完了通知が混線しない。
- 入場前の一フレーム表示や入力が漏れず、Awake / OnEnable が画面活動の開始を代行しない。
- Animator の外部中断、ループ、更新停止、対象消失、Time.timeScale が 0 の場合に、成功・取消・失敗を誤判定しない。
- 並行演出の一方の失敗後も他方を放置せず、停止後の即時反映を古い再生が上書きしない。
- 借用返却時は契約どおりのゲート・見た目へ戻し、借用物を破棄せず、無関係な業務状態も書き戻さない。
- Call の回答待機は従来どおり必要な終了と呼び出し元の復帰を待ち、演出中の View / DI スコープを先に解放しない。
- 既存の起動オーバーレイが接続時に一瞬消えず、ブロッカーの寿命が画面 Presenter に移らない。
- 独立した Region の遷移は並行でき、共有演出の競合は構成変更前に拒否される。

利用者が個別の表示登録手順をゲーム側 helper に複製する構成や、Unity に履歴・活動・復旧の実行機構を複製する構成にしない。

## 関連文書

- [Navigathena](../../30_technical/features/navigathena.md)
- [API と責務](../../30_technical/features/navigathena_class_design.md)
- [使い方](../../30_technical/features/navigathena_usage.md)
- [履歴への復帰](../../50_tickets/technical/navigathena_history_reentry.md)
- [結果付き画面呼び出し](../../50_tickets/technical/navigathena_screen_calls.md)
- [遷移演出](../../50_tickets/technical/navigathena_navigation_transitions.md)
