# Navigathena - Scene management framework for Unity

[![Build](https://github.com/mackysoft/Navigathena/actions/workflows/build.yaml/badge.svg)](https://github.com/mackysoft/Navigathena/actions/workflows/build.yaml) [![Release](https://img.shields.io/github/v/release/mackysoft/Navigathena)](https://github.com/mackysoft/Navigathena/releases) [![openupm](https://img.shields.io/npm/v/com.mackysoft.navigathena?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.mackysoft.navigathena/)

**Created by Hiroya Aramaki ([Makihiro](https://twitter.com/makihiro_dev))**

## What is Navigathena ?

Navigathena is a scene management framework designed for Unity.

- Scene transition control with basic history feature.
- High functionality, capable of supporting use cases that require more advanced control.
- Highly scalable, most elements can be extended to suit the project.
- Implemented based on a clean design concept, it provides a robust framework that can withstand large-scale development.

## <a id="index" href="#index"> Table of Contents </a>

- [📥 Installation](#installation)
- [🔰 Usage](#usage)
  - [Basic scene navigation](#basic-scene-navigation)
    - [SceneNavigator](#scene-navigator)
    - [SceneEntryPoint](#scene-entry-point)
  - [Transition director](#transition-director)
    - [Progress display](#progress-display)
    - [Interrupt operation](#interrupt-operation)
  - [Inter-scene data transfer](#inter-scene-data-transfer)
  - [Single scene launch](#single-scene-launch)
  - [Interrupt scene operation](#interrupt-scene-operation)
  - [Scene history editing](#scene-history-editing)
- [📚 Integrations](#integrations)
  - [Addressables System](#addressables-system)
  - [Dependency injection](#dependency-injection)
- [❓ FAQ](#faq)
  - [How to load multiple scenes (sub-scenes)?](#subscene-management)
  - [How to have a scene that is always present throughout the life of the application?](#root-scene)
- [✉ Help & Contribute](#help-and-contribute)
- [📔 Author Info](#author-info)
- [📜 License](#license)

# <a id="installation" href="#installation"> 📥 Installation </a>

## Requirement

Navigathena depends on [UniTask](https://github.com/Cysharp/UniTask), which supports async / await. Please import UniTask into your project first.

UniTask: https://github.com/Cysharp/UniTask

## Install via .unitypackage

Download any version from releases.

Releases: https://github.com/mackysoft/Navigathena/releases

## Install via PackageManager

Or, you can add this package by opening PackageManager and entering

```
https://github.com/mackysoft/Navigathena.git?path=Assets/MackySoft/MackySoft.Navigathena
```

from the `Add package from git URL` option.

## Install via Open UPM

Or, you can install this package from the [Open UPM](https://openupm.com/packages/com.mackysoft.navigathena/) registry.

More details [here](https://openupm.com/).

```
openupm add com.mackysoft.navigathena
```

# <a id="usage" href="#usage"> 🔰 Usage </a>

The following code illustrates the use of Navigathena basic elements.

```cs
using UnityEngine;
using UnityEngine.UI;
using System.Threading;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.SceneManagement;

// Defines the entry point for the scene.
public sealed class HomeSceneEntryPoint : SceneEntryPointBase
{

  // Define the scene to be used.
  static readonly ISceneIdentifier s_GameSceneIdentifier = new BuiltInSceneIdentifier("Game");

  [SerializeField]
  Button m_LoadGameButton;

  IDisposable m_Subscription;

  // The basic lifecycle of the scene is invoked. (OnInitialize, OnEnter, OnExit, OnFinalize...)
  protected override UniTask OnEnter (ISceneDataReader reader, CancellationToken cancellationToken)
  {
    m_Subscription = m_LoadGameButton.OnClickAsAsyncEnumerable().SubscribeAwait(_ => OnLoadGameButtonClick());
    return UniTask.CompletedTask;
  }

  protected override UniTask OnExit (ISceneDataWriter writer, CancellationToken cancellationToken)
  {
    m_Subscription?.Dispose();
    return UniTask.CompletedTask;
  }

  async UniTask OnLoadGameButtonClick ()
  {
    // Perform scene transition operations via GlobalSceneNavigator.
    await GlobalSceneNavigator.Instance.Push(s_GameSceneIdentifier);
  }
}
```

> The full Scripting API is [here](https://mackysoft.github.io/Navigahena/api/MackySoft.Navigahena.html).
> Scripting API: https://mackysoft.github.io/Navigahena/api/MackySoft.Navigahena.html

## <a id="basic-scene-navigation" href="#basic-scene-navigation"> Basic scene navigation </a>

Navigathena has two basic concepts for scene transitions: SceneNavigator and SceneEntryPoint.

### <a id="scene-navigator" href="#scene-navigator"> SceneNavigator </a>

Navigathena provides an interface called `ISceneNavigator` for managing and navigating scenes. This interface allows basic transition operations while handling the history of scenes.

Here's a simple usage example of `ISceneNavigator`:

```cs
ISceneIdentifier identifier = new BuiltInSceneIdentifier("Game");
await GlobalSceneNavigator.Instance.Push(identifier);
```

To specify a scene, the `ISceneIdentifier` interface is used. By default, `BuiltInSceneIdentifier` is implemented, which can be used for standard loading of scenes registered in Unity Build Settings.

> For examples using Addressables, refer to the [Integration with the Addressables System](#addressables) section below.

Below are the transition operations provided by `ISceneNavigator`.

```cs
interface ISceneNavigator
{
  // Load the specified scene and add it to the history.
  UniTask Push (ISceneIdentifier identifier, ITransitionDirector transitionDirector = null, ISceneData sceneData = null, IAsyncOperation interruptOperation = null, CancellationToken cancellationToken = default);

  // Remove the head scene in the history and load the previous one.
  UniTask Pop (ITransitionDirector overrideTransitionDirector = null, IAsyncOperation interruptOperation = null, CancellationToken cancellationToken = default);

  // Load the specified scene and overwrite the history with only that scene.
  UniTask Change (ISceneIdentifier identifier, ITransitionDirector transitionDirector = null, ISceneData sceneData = null, IAsyncOperation interruptOperation = null, CancellationToken cancellationToken = default);

  // Load the specified scene and overwrite the head of the history.
  UniTask Replace (ISceneIdentifier identifier, ITransitionDirector transitionDirector = null, ISceneData sceneData = null, IAsyncOperation interruptOperation = null, CancellationToken cancellationToken = default);

  // Reload then current scene.
  UniTask Reload (ITransitionDirector overrideTransitionDirector = null, IAsyncOperation interruptOperation = null, CancellationToken cancellationToken = default);
}
```

> These methods are actually implemented by extension methods for `ISceneNavigator`.

`SceneNavigator` is abstracted through an interface, so if unique processing is required in your project, it is possible to implement a custom SceneNavigator. (If not specified, `StandardSceneNavigator` will be used.)

```cs
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void Initialize ()
{
  // Register a custom SceneNavigator
  GlobalSceneNavigator.Instance.Register(new MyCustomSceneNavigator());
}
```

Since scene management typically uses the same logic from the beginning to the end of a game's lifespan, a singleton `GlobalSceneNavigator` component has been implemented. This wraps the registered `ISceneNavigator` and supports features such as the [single scene launch](#single-scene-launch) mentioned later.

Incidentally, the `GlobalSceneNavigator` inspector allows you to view the registered `ISceneNavigator` and its current history.

![](https://storage.googleapis.com/zenn-user-upload/af995c2c903c-20231023.png)


### <a id="scene-entry-point" href="#scene-entry-point"> SceneEntryPoint </a>

The SceneEntryPoint concept is used to observe the lifecycle of a scene. It is a component that can be placed only once in each scene and plays a role in observing events such as the start and end of a scene, as well as the transfer of data.

```cs
public interface ISceneEntryPoint
{

  // Called after the start of the transition direction.
  UniTask OnInitialize (ISceneDataReader reader, IProgress<IProgressDataStore> transitionProgress, CancellationToken cancellationToken);

  // Called after the end of the transition direction.
  UniTask OnEnter (ISceneDataReader reader, CancellationToken cancellationToken);

  // Called before the start of the transition direction.
  UniTask OnExit (ISceneDataWriter writer, CancellationToken cancellationToken);

  // Called after the start of the transition direction.
  UniTask OnFinalize (ISceneDataWriter writer, IProgress<IProgressDataStore> transitionProgress, CancellationToken cancellationToken);

#if UNITY_EDITOR
  // Called before `OnInitialize` in the first loaded scene when executed in the editor. (Editor only)
  UniTask OnEditorFirstPreInitialize (ISceneDataWriter writer, CancellationToken cancellationToken);
#endif
}
```

Basically, SceneEntryPoint is designed to inherit from `SceneEntryPointBase` or `ISceneEntryPoint` and override each event.

> For a method of implementation that does not depend on inheriting `MonoBehaviour`, please refer to the [Integration with Dependency Injection (DI)](#dependency-injection) section.

When a transition operation is called, the following processes are executed in sequence:

1. The `OnExit` of the origin `ISceneEntryPoint`
2. The `Start` of the `ITransitionHandle` created from the `ITransitionDirector` passed during the transition operation
3. The `OnFinalize` of the origin `ISceneEntryPoint`
4. Unloading of the origin scene
5. The `ExecuteAsync` of the `IAsyncOperation` passed during the transition operation
6. Loading of the destination scene
7. The `OnInitialize` of the destination `ISceneEntryPoint`
8. The `End` of the `ITransitionHandle` used in step 2
9. The `OnEnter` of the destination `ISceneEntryPoint`

The roles of each event will be explained in the topics that follow.

## <a id="transition-director" href="#transition-director"> Transition director </a>

One of the elements that enhance the gaming experience is the scene transition direction. This could range from a simple fade-in and fade-out to the display of tips or animations of mini characters running across the screen.

Regardless of how the implementation is done, you might animate the Canvas element, or you might load an additional transition direction scene. In any case, it's essential for the system to be flexible, meaning that different types of directions can be easily added or changed.

In Navigathena, the transition direction during scene transitions are defined by the `ITransitionDirector` interface, and you can pass the `ITransitionDirector` as an argument when performing a scene transition operation.

The specific flow is as follows: when a scene transition begins, the transition effect defined by the `ITransitionDirector` starts first. During this effect, the following processes are executed in order:

```cs
public interface ITransitionDirector
{
  ITransitionHandle CreateHandle ();
}

public interface ITransitionHandle
{
  UniTask Start (CancellationToken cancellationToken = default);
  UniTask End (CancellationToken cancellationToken = default);
}
```

The `ITransitionDirector` acts as a factory for producing `ITransitionHandle`, which is an interface to control the start and end of transition directions.

By extending based on this `ITransitionDirector` and `ITransitionHandle`, you can control your own unique transition directions.

Below is an implementation example of the `SimpleTransitionDirector`, which realizes a transition through a simple fade direction.

```cs
public sealed class SimpleTransitionDirector : ITransitionDirector
{

  readonly CanvasGroup m_CanvasGroup;

  public SimpleTransitionDirector (CanvasGroup canvasGroup)
  {
    m_CanvasGroup = canvasGroup;
  }

  public ITransitionHandle Create ()
  {
    return new SimpleTransitionHandle(m_CanvasGroup);
  }

  sealed class SimpleTransitionHandle : ITransitionHandle
  {

    readonly CanvasGroup m_CanvasGroup;

    public SimpleTransitionHandle (CanvasGroup canvasGroup)
    {
      m_CanvasGroup = canvasGroup;
    }

    public async UniTask Start (CancellationToken cancellationToken = default)
    {
      // Play fade-in with DOTween.
      await m_CanvasGroup.DOFade(1f, 1f).ToUniTask(cancellationToken: cancellationToken);
    }

    public async UniTask End (CancellationToken cancellationToken = default)
    {
      await m_CanvasGroup.DOFade(0f, 1f).ToUniTask(cancellationToken: cancellationToken);
    }
  }
}
```

```cs
// Load a new scene while executing the direction with SimpleTransitionDirector.
await GlobalSceneNavigator.Instance.Push(new BuiltInSceneIdentifier("MyScene"), new SimpleTransitionDirector(m_CanvasGroup));
```

### <a id="progress-display" href="#progress-display"> Progress display </a>

In direction during transitions, it's not uncommon to require effects that display progress percentages and effects that vary based on the progress percentage.

With Navigathena, during a transition direction, you can pass any data type through `IProgress<IProgressDataStore>`. Processes interjected during transition direction (such as `ISceneEntryPoint.OnInitialize`/`OnFinalize`, `IAsyncOperation.ExecuteAsync`) are given `IProgress<IProgressDataStore>`. By writing data into `IProgressDataStore` and notifying through `IProgress<IProgressDataStore>`, you can incorporate information like transition progress or messages that you want to present to the player into the direction.

Below is an example that extends the earlier mentioned `SimpleTransitionDirector` to support progress display.

```cs
// Defines an arbitrary data type to be used as progress information.
public readonly struct MyProgressData
{
  
  public float Progress { get; }
  public string Message { get; }

  public MyProgressData (float progress, string message)
  {
    Progress = progress;
    Message = message;
  }
}
```

```cs
public sealed class SimpleTransitionDirector : ITransitionDirector
{

  readonly CanvasGroup m_CanvasGroup;
  readonly Text m_ProgressText;
  readonly Text m_MessageText;
  readonly Slider m_ProgressSlider;

  public SimpleTransitionDirector (CanvasGroup canvasGroup, Text progressText, Text messageText, Slider progressSlider)
  {
    m_CanvasGroup = canvasGroup;
    m_ProgressText = progressText;
    m_MessageText = messageText;
    m_ProgressSlider = progressSlider;
  }

  public ITransitionHandle Create ()
  {
    return new SimpleTransitionHandle(m_CanvasGroup, m_ProgressText, m_MessageText, m_ProgressSlider);
  }

  // By implementing IProgress<IProgressDataStore>, it is possible to receive progress information during the transition direction.
  sealed class SimpleTransitionHandle : ITransitionHandle, IProgress<IProgressDataStore>
  {

    readonly CanvasGroup m_CanvasGroup;
    readonly Text m_ProgressText;
    readonly Text m_MessageText;
    readonly Slider m_ProgressSlider;

    public SimpleTransitionHandle (CanvasGroup canvasGroup, Text progressText, Text messageText, SLider progressSlider)
    {
      m_CanvasGroup = canvasGroup;
      m_ProgressText = progressText;
      m_MessageText = messageText;
      m_ProgressSlider = progressSlider;
    }

    public async UniTask Start (CancellationToken cancellationToken = default)
    {
      await m_CanvasGroup.DOFade(1f,1f).ToUniTask(cancellationToken: cancellationToken);
    }

    public async UniTask Complete (CancellationToken cancellationToken = default)
    {
      await m_CanvasGroup.DOFade(0f,1f).ToUniTask(cancellationToken: cancellationToken);
    }

    void IProgress<IProgressDataStore>.Report (IProgressDataStore progressDataStore)
    {
      // Extract MyProgressData from IProgressStore.
      if (progressDataStore.TryGetData(out MyProgressData myProgressData))
      {
        m_ProgressText.text = myProgressData.Progress.ToString("P0");
        m_MessageText.text = myProgressData.Message;
        m_ProgressSlider.value = myProgressData.Progress;
      }
    }
  }
}
```

```cs
// Pass SimpleTransitionDirector when performing the transition operation.
await GlobalSceneNavigator.Instance.Push(new BuiltInSceneIdentifier("MyScene"), new SimpleTransitionDirector(m_CanvasGroup, m_ProgressText, m_MessageText, m_ProgressSlider));
```

```cs
// ...

// When notifying progress, it gets notified up to the SimpleTransitionDirector.
protected override UniTask OnInitialize (ISceneDataReader reader, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
{
  ProgressDataStore<MyProgressData> store = new();

  progress.Report(store.SetData(new MyProgressData(0.5f, "Generate Map")));

  await m_MapGenerator.Generate(cancellationToken);

  progress.Report(store.SetData(new MyProgressData(1f, "Complete")));
}
```

To ensure versatility, data can be obtained through `IProgressDataStore.TryGetData<T>`. (Internally, it casts `IProgressDataStore` to `IProgressDataStore<T>` to extract the type.)
Although it's not type-safe, i deemed this implementation appropriate to prevent wasteful allocations due to boxing and to avoid losing simplicity due to the propagation of generics.

When a scene is loaded or unloaded, by default, the `StandardSceneNavigator` stores `LoadSceneProgressData` in `IProgressDataStore`. The behavior here can also be customized from the constructor of `StandardSceneNavigator`.

```cs
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void Initialize ()
{
  // Initialize and register the StandardSceneNavigator with our own MySceneProgressFactory.
  GlobalSceneNavigator.Instance.Register(new StandardSceneNavigator(TransitionDirector.Empty(), new MySceneProgressFactory()));
}
```

### <a id="interrupt-operation" href="#interrupt-operation"> Interrupt operation </a>

When scene transitioning, it is sometimes necessary to insert a pre-load process or some kind of pre-check process.

Navigathena define the `IAsyncOperation` interface to perform `processes to be performed between the unloading of the current scene and the loading of the next scene`. The `IAsyncOperation` can be passed during each transition operation in `ISceneNavigator`.

```cs
IAsyncOperation op = m_PreloadAsyncOperation;
await GlobalSceneNavigator.Push(nextScene, interruptOperation: op);
```

The structure of `IAsyncOperation` is very simple, it defines functions to perform asynchronous operations. Since ``IProgress<IProgressDataStore>` is passed as an argument, it can be integrated with the progress display during transition direction.

```cs:IAsyncOperation.cs
public interface IAsyncOperation
{
  UniTask ExecuteAsync (IProgress<IProgressDataStore> progress, CancellationToken cancellationToken = default);
}
```

Basically, you would define a type that implements `IAsyncOperation`, but there are also some convenience functions for convenient handling.

```cs
// Create an anonymous IAsyncOperation
IAsyncOperation operation = AsyncOperation.Create(async (progress, cancellationToken) => {
  // Asynchronous processing of some kind
  await DoSomething(progress, cancellationToken);
});

// Merge multiple IAsyncOperations
IAsyncOperation compositeOperation = AsyncOperation.Combine(op1, op2, op3);
```

## <a id="inter-scene-data-transfer" href="#inter-scene-data-transfer"> Inter-scene data transfer </a>

When creating a game, we will encounter situations where you want to pass values to the next scene. A common case is "I want to pass the ID of a selected element (stage, character, etc.) to the next scene".

In the `ISceneEntryPoint` callback, `ISceneDataReader` is passed to `OnInitialize` / `OnEnter` and `ISceneDataWriter` to `OnExit` / `OnFinalize`. These interfaces allow data to be transferred between scenes.

For example, by passing a data type implementing the `ISceneData` interface as an argument to `ISceneNavigator.Push`, you can transfer that data to `OnInitialize` and `OnEnter` of the destination `ISceneEntryPoint`.

On the other hand, the `ISceneDataWriter` is passed when leaving a scene. This allows the implementation of a process that stores the state of the scene when leaving the scene in the scene history and restores the state of the scene when returning to that scene.

For example, when a user transitions to the next scene with "a specific screen open in the scene" and returns to the previous scene, the data written to `ISceneDataWriter` is passed to `OnInitialize` / `OnEnter`, so at scene initialization, information on "which screen was open can be obtained to restore the state of the scene.

```cs
public sealed class HomeSceneData : ISceneData
{
  public HomeScreenType LastDisplayedScreenType { get; init; }
}

public sealed class HomeSceneEntryPoint : SceneEntryPoint
{

  [SerializeField]
  HomeView m_View;

  protected override UniTask OnInitialize (ISceneDataReader reader, CancellationToken cancellationToken)
  {
    // At scene startup, retrieve the saved state and apply it to the elements in the scene.
    if (reader.TryRead(out HomeSceneData sceneData))
    {
      m_View.SetScreen(sceneData.LastDisplayedScreenType);
    }
  }

  protected override UniTask OnFinalize (ISceneWriter writer, CancellationToken cancellationToken)
  {
    // Store data when leaving a scene
    writer.Write(new HomeSceneData
    {
      LastDisplayedScreenType = m_View.ScreenType
    });
  }
}
```

> It is recommended that `ISceneData` type definitions be unified as "one per scene" to avoid compromising type safety.

## <a id="single-scene-launch" href="#single-scene-launch"> Single scene launch </a>

When debugging with the Unity editor, it is sometimes important for debugging efficiency to be able to "successfully launch the game from any scene".

In `ISceneEntryPoint`, there is an editor-specific callback called `OnEditorFirstPreInitialize` that is called on the "first scene launched on the editor". With this callback, the `ISceneDataWriter` is passed when it is invoked the very first time, so that initial data can be written and passed to subsequent `OnInitialize` / `OnEnter`.

```cs
#if UNITY_EDITOR
protected override UniTask OnEditorFirstPreInitialize (ISceneDataWriter writer, CancellationToken cancellationToken)
{
  writer.Write(new IngameSceneData
  {
    CharacterId = m_CharacterId
  });
  return UniTask.CompletedTask;
}
#endif
```

> The `OnEditorFirstPreInitialize` must be enclosed in a `UNITY_EDITOR` directive.

## <a id="interrupt-scene-operation" href="#interrupt-scene-operation"> Interrupt scene operation </a>

Occasionally, during the processing of a scene transition, you may want to override it with a transition process to another scene.

For example, there are cases such as "I want to transition to an event scene, but when I check `OnInitialize`, the event period has ended, so I want to call `Pop` in `OnInitialize` to return the scene. In this case, the current transition process is interrupted and the scene transition operation is interrupted, but if this is implemented correctly, the internal state management is not easy.

Navigathena is implemented to handle such interruptions.
The following is a simple example of actual operation.

```cs:EventSceneEntryPoint.cs
protected override async UniTask OnInitialize (ISceneDataReader reader, IProgress<IProgressDataStore> progress, CancellationToken cancellationToken)
{
  // Assume the event has expired.
  bool isExpired = true;

  if (isExpired) {
    // NOTE: The cancellationToken is not passed to the transition operation because the cancellation state is canceled when the transition operation is performed.
    await GlobalSceneNavigator.Instance.Pop(CancellationToken.None);

    // true
    Debug.Log(cancellationToken.IsCancellationRequested);
  }
}

protected override UniTask OnEnter (ISceneDataReader reader, CancellationToken cancellationToken)
{
  Debug.Log("EventSceneEntryPoint.OnEnter");
  return UniTask.CompletedTask;
}
```

When a new transition process is interrupted, the `CancellationTokenSource` managed in the SceneNavigator side is canceled, and the `CancellationToken` passed in each event is also canceled. In the above example, after calling `Pop`, the `cancellationToken` will be in the cancel request state, and the subsequent `OnEnter` will not be called.

## <a id="scene-history-editing" href="#scene-history-editing"> Scene history editing </a>

Occasionally, there are situations where you want to build a history that ignores the current history.

Navigathena provides an `ISceneHistoryBuilder` to directly manipulate the history of the `ISceneNavigator` by using the `IUnsafeSceneNavigator.GetHistoryBuilderUnsafe` method.

```cs
using MackySoft.Navigathena.SceneManagement;
using MackySoft.Navigathena.SceneManagement.Unsafe; // Required for IUnsafeSceneNavigator features

//...

// Remove all except the current scene from the current history, add the Home scene as one previous scene, and reconstruct the history.
GlobalSceneNavigator.Instance.GetHistoryBuilderUnsafe()
  .RemoveAllExceptCurrent()
  .Add(new SceneHistoryEntry(SceneDefinitions.Home, TransitionDefinitions.Loading, new SceneDataStore())))
  .Build();

// Pop to return to the Home scene.
await GlobalSceneNavigator.Instance.Pop();
```

The `ISceneHistoryBuilder` you have retrieved will be unavailable after the `ISceneNavigator` transition operation is performed, because it is a different version. So it is necessary to complete the history manipulation process in `ISceneHistoryBuilder` before the transition operation is performed.

This functionality is not included in `ISceneNavigator`, but is defined in `IUnsafeSceneNavigator`. If you implement a custom `ISceneNavigator` and need scene history manipulation functionality, please implement an additional `IUnsafeSceneNavigator`. (`GlobalSceneNavigator` and `StandardSceneNavigator` explicitly implement `IUnsafeSceneNavigator`)

> As the name implies, `IUnsafeScenNavigator` is an Unsafe feature and i do not recommend its heavy use.

# <a id="integrations" href="#integrations"> 📚 Integrations </a>

## <a id="addressables" href="#addressables"> Addressables System </a>

In Navigathena, the low-level handling of loading and unloading of each scene is handled by the `ISceneIdentifier` interface.

By default, only the `BuiltInSceneIdentifier` is implemented, but if your project contains the [Addressables"](https://docs.unity3d.com/Packages/com.unity.addressables@2.0/manual/index.html) package is included in the project, the `AddressalbeSceneIdentifier` will be available, allowing seamless incorporation of loading and unloading of scenes handled as Addressables.

Below is an example of its use in an actual game I am working on.

```cs
using MackySoft.Navigathena.SceneManagement;
using MackySoft.Navigathena.SceneManagement.AddressableAssets;

// Definition of scenes used in the project
public static class SceneDefinitions
{

  // Use BuiltInSceneIdentifier for splash
  public static ISceneIdentifier Splash { get; } = new BuiltInSceneIdentifier("Splash");

  // For post-splash scenes, use AddressableSceneIdentifier because Addressables dependencies need to be resolved
  // Pass `object key` as argument
  public static ISceneIdentifier Title { get; } = new AddressableSceneIdentifier("Title");
  public static ISceneIdentifier Introduction { get; } = new AddressableSceneIdentifier("Introduction");
  public static ISceneIdentifier Home { get; } = new AddressableSceneIdentifier("Home");
  public static ISceneIdentifier Game { get; } = new AddressableSceneIdentifier("Game");
}
```

Since it is encapsulated in the `ISceneIdentifier` interface, you do not need to worry about whether the scene you are using is built-in or addressable.
The contents of `ISceneIdentifier` are simple: `CreateHandle` returns an `ISceneHandle`, and the returned `ISceneHandle` is responsible for the actual loading and unloading process.

```cs
public interface ISceneIdentifier
{
  ISceneHandle CreateHandle ();
}

public interface ISceneHandle
{
  UniTask<Scene> Load (IProgress<float> progress = null, CancellationToken cancellationToken = default);
  UniTask Unload (IProgress<float> progress = null, CancellationToken cancellationToken = default);
}
```

In the case of `AddressableSceneIdentifier`, it wraps the `LoadSceneAsync`/`UnloadSceneAsync` of Addressables, which of course resolves asset dependencies. However, since downloading assets is beyond the scope of the scene transition framework responsibilities, it is recommended to incorporate pre-download logic at appropriate times depending on the convenience of the project.

## <a id="dependency-injection" href="#dependency-injection"> Dependency injection (DI) </a>

The scene management framework can easily become a central hub that influences the very foundation of the architecture in game development, such as "who will oversee the logic of the scene?".

Navigathena can support dependency injection by incorporating functional integration with a DI container built upon SceneEntryPoint. ["VContainer"](https://github.com/hadashiA/VContainer) is supported by default.

In this integration, instead of inheriting from SceneEntryPoint, you can register a type that implements the ISceneLifecycle interface with the DI container. By placing the pre-defined `ScopedSceneEntryPoint` by Navigathena in the scene, it is possible to decouple the scene's lifecycle from the component (`MonoBehaviour`). 

Due to the specification of the timing at which VContainer container determines its parent, it's a bit hacky, but it's necessary to set the LifetimeScope GameObject to inactive. You can easily create this from the `Create LifetimeScope` button displayed in the `ScopedSceneEntryPoint` inspector.

```cs
public override void Configure (IContainerBuilder builder)
{
  builder.Register<ISceneLifecycle, TitleSceneLifecycle>(Lifetime.Singleton);
}
```

# <a id="faq" href="#faq"> ❓ FAQ </a>

## <a id="subscene-management" href="#subscene-management"> How to load multiple scenes (sub-scenes)? </a>

Consider a scenario where, upon loading an in-game scene, you might want to additionally load a sub-scenes that includes a stage or UI.

```
- Ingame
  - HUD
  - Stage_01
```

Occasionally, sub-scenes are managed by a scene management wrapper, but Navigathena does not explicitly support such sub-scene features.

The logic for scene transitions inherently needs to be aware of the destination scene. Yet, it is preferable not to know "what kind of logic will be executed in the destination". When the logic in the originating scene becomes complex, the responsibility of "who manages the scene" becomes ambiguous. This increases the risk of producing code that is difficult to maintain.

In Navigathena, it is recommended to clarify this responsibility by "transferring data such as stage IDs between scenes and managing sub-scenes using the SceneEntryPoint (and SceneLifecycle) in the destination scene with UnityEngine.SceneManagement (or similar functionality)". By assigning the responsibility of managing sub-scenes to the destination scene, each scene can independently determine which sub-scenes it has and the logic for loading and unloading them.

## <a id="root-scene" href="#root-scene"> How to have a scene that is always present throughout the life of the application? </a>

While there's a personal preference involved, having a scene that always persists throughout the application lifespan (which we call the Root scene) is convenient for development. While one can use `DontDestroyOnLoad`, its challenging aspect is its lack of flexibility in handling.

In projects under development, I have adopted an approach where I am extend `ScopedSceneEntryPoint` and load the Root scene during the `EnsureParentScope` phase.

```cs
public sealed class MyProjectScopedSceneEntryPoint : ScopedSceneEntryPoint
{

  const string kRootSceneName = "Root";

  protected override async UniTask<LifetimeScope> EnsureParentScope (CancellationToken cancellationToken)
  {
    // Load root scene.
    if (!SceneManager.GetSceneByName(kRootSceneName).isLoaded)
    {
      await SceneManager.LoadSceneAsync(kRootSceneName, LoadSceneMode.Additive)
        .ToUniTask(cancellationToken: cancellationToken);
    }

    Scene rootScene = SceneManager.GetSceneByName(kRootSceneName);

#if UNITY_EDITOR
    // Reorder root scene.
    EditorSceneManager.MoveSceneBefore(rootScene, gameObject.scene);
#endif

    // Build root LifetimeScope container.
    if (rootScene.TryGetComponentInScene(out LifetimeScope rootLifetimeScope, true) && rootLifetimeScope.Container == null)
    {
      await UniTask.RunOnThreadPool(() => rootLifetimeScope.Build(), cancellationToken: cancellationToken);
    }
    return rootLifetimeScope;
  }
}
```

# <a id="help-and-contribute" href="#help-and-contribute"> ✉ Help & Contribute </a>

I welcome feature requests and bug reports in [issues](https://github.com/mackysoft/Navigathena/issues) and [pull requests](https://github.com/mackysoft/Navigathena/pulls).

If you feel that my works are worthwhile, I would greatly appreciate it if you could sponsor me. Private sponsor and one-time donate are also welcome.

GitHub Sponsors: https://github.com/sponsors/mackysoft

# <a id="author-info" href="#author-info"> 📔 Author Info </a>

Hiroya Aramaki is a indie game developer in Japan.

- Twitter: [https://twitter.com/makihiro_dev](https://twitter.com/makihiro_dev)

# <a id="license" href="#license"> 📜 License </a>

This library is under the [MIT License](https://github.com/mackysoft/Navigathena/blob/main/LICENSE).
