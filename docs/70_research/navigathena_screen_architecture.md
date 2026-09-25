---
authority: reference
---

# 画面管理の責務とライフサイクルの比較調査

## 調査の目的と範囲

Navigathena の画面契約、活動開始・停止、表示アダプター、カスタムブロッカーの境界を判断するための資料とする。
調査日は 2026-09-13 とする。
公式ドキュメント、公開されたライブラリ実装、公式リポジトリ内のサンプルを対象とした。
非公開の商用ゲームの内部設計や、採用数・品質の順位までは確認していない。
Epic の Lyra については、公式資料が説明する CommonUI の使用方法を参照し、Lyra のソースを直接監査した結果とは区別する。

GitHub の実装は次の revision に固定した。
開発ブランチの観測点であり、推奨リリース番号を意味しない。

| 対象 | revision |
| --- | --- |
| UnityScreenNavigator | `c7c0c367f82811f6077cc27bf7739c3c058a4a6b` |
| Caliburn.Micro | `bde86e9f3c507a976a405e2443cded9e5f43fee7` |
| Prism | `358118cd640d9a22ff8cf21c8ad197fa038b7990` |
| Flutter の stable ブランチ | `9584c6713b324636289d067944a46fd6b49df14b` |

## 比較した責務

| 対象 | 管理側が担うこと | 利用側が処理を書く場所 | そのまま移植できない前提 |
| --- | --- | --- | --- |
| [Caliburn.Micro](https://caliburnmicro.com/documentation/composition) | Conductor が対象の活動・停止・閉鎖を調整し、View の解決と接続は別の仕組みが行う | Screen の役割を担う ViewModel、Presenter、通常のオブジェクトなど | Screen は役割であり、特定のクラス構成の指定ではない。活動開始は View の表示完了を保証しない |
| [Prism](https://docs.prismlibrary.com/docs/current/platforms/wpf/view-composition/) | Region、活動通知、非活動後の保持、具体的な UI control との接続を分担する | View または ViewModel の対応する契約 | WPF の DataContext の探索を使う。Region の意味は Navigathena の履歴単位と完全には一致しない |
| [ReactiveUI](https://www.reactiveui.net/documentation/handbook/when-activated/) | 活動期間ごとの購読を集め、期間の終了に対応して解放する | ViewModel と View のそれぞれに必要な購読・処理 | View の接続による活動通知と、モーダルによる下層の活動停止は同義ではない |
| [CommonUI](https://dev.epicgames.com/documentation/unreal-engine/commonui-input-technical-guide-for-unreal-engine) | Widget の構成と Action Router が入力の配送先を決める | Activatable Widget の callback、入力設定、ゲーム側の処理 | Slate／UMG の階層・描画順・フォーカスが前提。任意のゲームロジックを一括停止する仕組みではない |
| [UnityScreenNavigator の Modal](https://github.com/Haruma-K/UnityScreenNavigator/blob/c7c0c367f82811f6077cc27bf7739c3c058a4a6b/Assets/UnityScreenNavigator/Runtime/Core/Modal/Modal.cs#L162) | Container と内部の lifecycle handler が入退場・演出・終了を進める | 画面 component、または登録した外部 lifecycle 実装 | uGUI が前提。操作ごとの callback 群と Navigathena の合成状態は一対一ではない |
| [Flutter の ModalRoute](https://api.flutter.dev/flutter/widgets/ModalRoute-class.html) | Navigator と Route が履歴、本文、barrier、遷移を管理する | Widget の State、Route のカスタム実装、RouteAware の通知先 | Flutter の Route は表示を持つ。データだけの Navigathena.Route とは責務が異なる |
| [Android Navigation](https://developer.android.com/guide/navigation/use-graph/programmatic) | NavBackStackEntry のライフサイクルと ViewModel の保持範囲を管理する | 必要なライフサイクルを選んだ observer、ViewModel、画面の処理 | View、履歴 Entry、ViewModel の寿命は別。OS と UI 基盤の状態をそのままゲーム内状態に置き換えない |

## ロジック主体と通知契約は区別されている

Caliburn.Micro は Screen をプレゼンテーション層の役割として説明し、Conductor がその活動を調整する。
ViewModel の基底クラスとして使うことが多いが、Presenter や別の通常オブジェクトも対象にできる。
したがって、ライフサイクル契約を実装したことだけから「その型がすべての画面ロジックを直接持つ」とは決まらない。
[Screen の実装](https://github.com/Caliburn-Micro/Caliburn.Micro/blob/bde86e9f3c507a976a405e2443cded9e5f43fee7/src/Caliburn.Micro.Core/Screen.cs#L96)では、初期化済み・活動中を管理し、活動ごとの処理と閉鎖を分けている。

Prism の [RegionActiveAwareBehavior](https://github.com/PrismLibrary/Prism/blob/358118cd640d9a22ff8cf21c8ad197fa038b7990/src/Wpf/Prism.Wpf/Navigation/Regions/Behaviors/RegionActiveAwareBehavior.cs#L54) は、Region の活動集合の変更を View と ViewModel へ伝える。
通知を受ける契約と、Region の活動集合を決める側は異なる。
[ナビゲーション参加の説明](https://docs.prismlibrary.com/docs/current/navigation/regions/view-viewmodel-participation/)でも、遷移通知と、非活動後に実体を保持する方針は別の契約になっている。

UnityScreenNavigator の公式リポジトリには、[外部 Presenter を画面の lifecycle に登録する実装](https://github.com/Haruma-K/UnityScreenNavigator/blob/c7c0c367f82811f6077cc27bf7739c3c058a4a6b/Assets/Demo/Subsystem/PresentationFramework/UnityScreenNavigatorExtensions/ModalPresenter.cs#L196)がある。
これは利用側の構成例であり、ライブラリが全利用者へ Presenter 基底クラスを要求しているわけではない。

これらから導けるのは、管理側がタイミングと順序を担い、対象が自身の処理を実装または委譲する境界である。
常に Screen、Lifecycle、Presenter の三つのオブジェクトを作る必要がある、という結論にはならない。

## 保持・表示・活動・入力配送は異なる

CommonUI の [WidgetStack](https://dev.epicgames.com/documentation/unreal-engine/API/Plugins/CommonUI/UCommonActivatableWidgetStack?lang=en-US) は、その stack の最上段だけを表示・活動させる。
一方、[複数の UI 層を扱う説明](https://dev.epicgames.com/documentation/unreal-engine/overview-of-advanced-multiplatform-user-interfaces-with-common-ui-for-unreal-engine)では、下層の Widget を活動状態のまま残し、最前面の入力配送先を切り替えられる。
したがって CommonUI の Active を、Navigathena の「上層によって操作を遮られた画面のロジックが停止している状態」と同一視できない。
[Input Config](https://dev.epicgames.com/documentation/en-us/unreal-engine/input-fundamentals-for-commonui-in-unreal-engine) は移動・視点操作などの入力設定を扱い、非活動化時に前の設定を復帰させる。
この処理も、任意の購読やゲーム進行を停止する責務ではない。

Android Navigation は、dialog の下に見えている Entry を STARTED、最前面を RESUMED として扱う。
dialog が重なっても Fragment 自身は RESUMED のままになり得るため、対象 Entry のライフサイクルを使うよう公式資料が注意している。
つまり、実装 component の有効状態から画面の活動状態を代用すると、対象を取り違える。
[ライフサイクルと非同期処理の説明](https://developer.android.com/topic/libraries/architecture/coroutines)では、ViewModel の寿命、Composition の寿命、状態に応じた購読期間も区別される。
Composition に存在することは、実際に見えていることを必ずしも意味しない。

ReactiveUI では、活動中だけ必要な購読を活動期間の終了時に解放できる。
これは購読先の共有サービスそのものを破棄することではない。
Navigathena でも、操作の購読、表示値を更新する購読、画面をまたぐ保存処理を同じ寿命へ押し込めるべきではない。

## 非同期ライフサイクルは管理側が完了を扱う

Caliburn.Micro の [4.0 の説明](https://caliburnmicro.com/documentation/4.0.0/screen)は、非同期初期化の完了前に活動処理が始まる問題を、待機可能なライフサイクルで解決している。
観測した [Conductor の実装](https://github.com/Caliburn-Micro/Caliburn.Micro/blob/bde86e9f3c507a976a405e2443cded9e5f43fee7/src/Caliburn.Micro.Core/ConductorBaseWithActiveItem.cs#L40)も、旧対象の停止と新対象の活動を待機する。
ただし ActiveItem の更新位置を含め、Navigathena と同じ確定・復元保証を持つとは主張しない。
概説に古い同期メソッド名が残るため、公開シグネチャの確認には実装とバージョン別資料を併用した。

UnityScreenNavigator の [ModalLifecycleHandler](https://github.com/Haruma-K/UnityScreenNavigator/blob/c7c0c367f82811f6077cc27bf7739c3c058a4a6b/Assets/UnityScreenNavigator/Runtime/Core/Modal/ModalLifecycleHandler.cs#L26) は、入退場前の処理、演出、通知、終了処理を段階に分ける。
[SettingsModalPresenter](https://github.com/Haruma-K/UnityScreenNavigator/blob/c7c0c367f82811f6077cc27bf7739c3c058a4a6b/Assets/Demo/Core/Scripts/Presentation/Setting/SettingsModalPresenter.cs#L80)では、退場時に設定保存を待機する利用例を確認した。
すべての購読を活動期間で切っているサンプルではなく、非同期の退場処理を置けることの証拠として扱う。

ここから、Navigathena の公開契約を同期 callback だけに制限する理由は得られない。
同期の入力遮断と、画面処理の停止完了を待つ非同期段階を分ければ、Core の確定区間に await を入れずに両立できる。
取消要求を出したことと、処理が停止したことは別なので、使用中の View を先に解放してはならない。
これは比較結果に基づく Navigathena 向けの設計判断であり、比較先に同じ障害回復保証があるという意味ではない。

## ブロッカーのカスタムと寿命管理は別の拡張点になる

UnityScreenNavigator の [ModalContainer](https://github.com/Haruma-K/UnityScreenNavigator/blob/c7c0c367f82811f6077cc27bf7739c3c058a4a6b/Assets/UnityScreenNavigator/Runtime/Core/Modal/ModalContainer.cs#L78) は、共通設定または Container の上書きから backdrop を選び、内部 handler に管理を委ねる。
[ChangeOrderModalBackdropHandler](https://github.com/Haruma-K/UnityScreenNavigator/blob/c7c0c367f82811f6077cc27bf7739c3c058a4a6b/Assets/UnityScreenNavigator/Runtime/Core/Modal/ModalBackdropHandler/ChangeOrderModalBackdropHandler.cs#L30) は、一つの backdrop の表示順を変えて再使用する。
最初の Modal では生成・登場、追加の Modal では順序変更、最後の退場後には破棄する。
これは「再使用の調整は画面ではなく管理側が行う」という具体例である。
world の寿命中ずっと保持する方式や、Route ごとのカスタム切替まで同一ではない。

Flutter の [buildModalBarrier](https://api.flutter.dev/flutter/widgets/ModalRoute/buildModalBarrier.html) は、barrier の構成をカスタムできる入口である。
[ModalRoute の実装](https://github.com/flutter/flutter/blob/9584c6713b324636289d067944a46fd6b49df14b/packages/flutter/lib/src/widgets/routes.dart#L2350)は本文と barrier の OverlayEntry を生成し、[OverlayRoute](https://github.com/flutter/flutter/blob/9584c6713b324636289d067944a46fd6b49df14b/packages/flutter/lib/src/widgets/routes.dart#L97)がそれらを終了する。
barrier は独立した履歴項目ではないが、通常は Route ごとの構成であり、world 共通の同一実体ではない。
Flutter の Route は表示管理オブジェクトなので、この入口を Navigathena のデータ型 Route にそのまま追加してはならない。
また、barrier の入力とアニメーションを関連付ける箇所があり、Navigathena の即時切替・演出非依存の遮断保証とは分けて扱う。

今回の要求に対しては、共通設定と Route ごとの生成登録からカスタム実装を選び、その管理を Runtime に残す構成が妥当である。
共通実体の world 単位の保持、ブロッカー同士の即時切替、フル画面での非表示は、利用者から指定された Navigathena 固有の要件として設計する。

## Navigathena の設計へ反映する判断

| 判断 | 根拠 | 採用しない解釈 |
| --- | --- | --- |
| 画面単位の契約に準備・活動・停止・終了を持たせる | Caliburn.Micro の Screen の役割と、UnityScreenNavigator の管理された lifecycle | メソッドが複数あるだけで、必ず別々の管理オブジェクトに分割する |
| ゲーム側の対象が処理を実装または委譲する | 外部 Presenter、ViewModel への通知 | インターフェース名からゲーム側のクラス構成を決める |
| 活動期間は画面構成から導出する | Android の Entry の状態と、CommonUI の入力配送との相違 | MonoBehaviour の有効状態や一時的な入力 gate を活動状態にする |
| 非同期の開始・停止を Runtime が待機する | Caliburn.Micro の非同期 lifecycle と UnityScreenNavigator の退場処理 | Core の同期確定を理由に、利用側へ待機しない処理を強いる |
| UI の具体的な反映はアダプターへ置く | Prism の RegionAdapter と CommonUI の基盤依存の範囲 | 共通契約が UIDocument／Canvas の二択を持ち続ける |
| ブロッカーの生成登録と管理を画面本文から分離する | UnityScreenNavigator の内部 handler と Flutter のカスタム barrier | カスタム可能にするため画面ロジックへ寿命管理を渡す |

これらは比較から導いた設計判断であり、外部ライブラリの共通仕様そのものではない。
Navigathena には、既存の発行元の失効、確定前の復帰、確定後の障害扱い、資源依存の保持も必要である。
比較先の通常遷移のコードだけでは、それらの失敗時の保証を代替できない。
具体的な提案と外部動作の受入条件は、[アーキテクチャと利用契約](../50_tickets/technical/navigathena_architecture.md)に置く。
