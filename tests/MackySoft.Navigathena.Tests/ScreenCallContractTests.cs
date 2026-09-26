using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.MicrosoftDI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class ScreenCallContractTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Restarting_the_call_owner_cancels_nested_waits_and_does_not_resume_the_removed_owner ()
    {
        await using Harness game = new(homeLower: LowerPresentationPolicy.HideAndRetain);
        await game.Host.StartAsync(new HomeRoute("old"));
        HomeScreen old = game.Home;
        bool continued = false;
        ScreenWork work = old.Activity!.StartWork(async context =>
        {
            await context.Navigation.InvokeAsync(new QuestionRoute(1));
            continued = true;
        });
        await game.WaitForTopAsync<QuestionRoute>();
        ScreenActivityContext<bool> question = game.Question.Activity!;
        await Assert.ThrowsAsync<NavigationConfigurationException>(() => question.Navigation.ReplaceFromAsync<HomeRoute>(new HomeRoute("new")));
        await question.Navigation.GetRegionNavigation(RegionTarget.Root).ReplaceFromAsync<HomeRoute>(
            new HomeRoute("new"), new NavigationOptions { RecreateInstance = true }).WaitAsync(TestTimeout);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync().WaitAsync(TestTimeout));
        Assert.False(continued);
        Assert.Equal(1, old.Disposed);
        Assert.Equal(1, game.Question.Disposed);
        Assert.NotSame(old, game.Home);
        Assert.Equal(new HomeRoute("new"), game.Home.Route);
        Assert.Single(game.Host.State.Current.Entries);
        Assert.Throws<InvalidOperationException>(() => question.Call.Complete(true));
    }

    [Fact]
    public async Task Explicit_replacement_of_a_nested_call_keeps_the_enclosing_answer_contract ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        Task<bool> outer = game.Host.Client.InvokeAsync(game.Host.Root, new QuestionRoute(1));
        await game.WaitForTopAsync<QuestionRoute>();
        Task<string?> inner = game.Question.Activity!.Navigation.InvokeAsync(new NameRoute());
        await game.WaitForTopAsync<NameRoute>();
        await game.Name.Activity!.Navigation.GetRegionNavigation(RegionTarget.Root)
            .ReplaceFromAsync<NameRoute>(new DetailsRoute()).WaitAsync(TestTimeout);
        Assert.False(outer.IsCompleted);
        // The owner is still covered by Details, so its cancelled call settles on return.
        await game.Details.Activity!.Navigation.BackAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inner.WaitAsync(TestTimeout));
        game.Question.Activity!.Call.Complete(true);
        Assert.True(await outer.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task Ordinary_call_can_wait_for_a_nested_answer_and_then_close_without_a_result ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        ScreenActivityContext caller = game.Home.Activity!;
        ScreenWork flow = caller.StartWork(async work =>
        {
            await work.Navigation.InvokeAsync(new DetailsRoute());
            Assert.NotSame(caller, game.Home.Activity);
            Assert.True(game.Home.View.Presentation.InputEnabled);
            Assert.Equal(1, game.Details.Disposed);
            Assert.Equal(1, game.Name.Disposed);
            game.Home.View.Text = "Finished";
        });
        await game.WaitForTopAsync<DetailsRoute>();
        ScreenWork nested = game.Details.Activity!.StartWork(async work =>
        {
            string? answer = await work.Navigation.InvokeAsync(new NameRoute());
            Assert.Null(answer);
            game.Details.View.Text = "Later";
        });
        await game.WaitForTopAsync<NameRoute>();
        Assert.False(flow.WaitAsync().IsCompleted);
        game.Name.Activity!.Call.Complete(null);
        await nested.WaitAsync().WaitAsync(TestTimeout);
        Assert.Equal("Later", game.Details.View.Text);
        Assert.False(flow.WaitAsync().IsCompleted);
        await game.Details.Activity!.Navigation.BackAsync();
        await flow.WaitAsync().WaitAsync(TestTimeout);
        Assert.Equal("Finished", game.Home.View.Text);
    }

    [Fact]
    public async Task Ordinary_call_waits_for_cleanup_before_completing ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        TaskCompletionSource<bool> terminating = Signal<bool>();
        TaskCompletionSource<bool> release = Signal<bool>();
        game.Details.OnTerminate = async () =>
        {
            terminating.TrySetResult(true);
            await release.Task;
        };
        Task call = game.Host.Client.InvokeAsync(game.Host.Root, new DetailsRoute());
        await game.WaitForTopAsync<DetailsRoute>();
        game.Details.Activity!.Navigation.Back();
        await terminating.Task.WaitAsync(TestTimeout);
        try
        {
            Assert.False(call.IsCompleted);
            Assert.Equal(0, game.Details.Disposed);
        }
        finally
        {
            release.TrySetResult(true);
        }
        await call.WaitAsync(TestTimeout);
        Assert.Equal(1, game.Details.Disposed);
    }

    [Fact]
    public async Task Ordinary_call_cancellation_closes_the_screen_without_continuing_the_flow ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        using CancellationTokenSource cancellation = new();
        Task call = game.Host.Client.InvokeAsync(game.Host.Root, new DetailsRoute(), cancellation.Token);
        await game.WaitForTopAsync<DetailsRoute>();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TestTimeout));
        Assert.Equal(1, game.Details.Disposed);
        Assert.True(game.Home.View.Presentation.InputEnabled);
    }

    [Fact]
    public async Task Ordinary_call_propagates_failure_to_restore_the_caller ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        Task call = game.Host.Client.InvokeAsync(game.Host.Root, new DetailsRoute());
        await game.WaitForTopAsync<DetailsRoute>();
        game.Home.OnActivate = _ => throw new InvalidOperationException("Cannot resume.");
        game.Details.Activity!.Navigation.Back();
        ScreenCallException failure = await Assert.ThrowsAsync<ScreenCallException>(() => call.WaitAsync(TestTimeout));
        Assert.Equal(ScreenCallFailureStage.Returning, failure.Stage);
        Assert.False(failure.AnswerCommitted);
    }

    [Fact]
    public async Task Work_completion_includes_the_continuation_after_invoke ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        TaskCompletionSource<bool> continued = Signal<bool>();
        TaskCompletionSource<bool> release = Signal<bool>();
        ScreenWork flow = game.Home.Activity!.StartWork(async work =>
        {
            bool answer = await work.Navigation.InvokeAsync(new QuestionRoute(1));
            Assert.False(answer);
            continued.TrySetResult(true);
            await release.Task;
            Assert.Equal(0, game.Home.Disposed);
            game.Home.View.Text = "Applied";
        });
        await game.WaitForTopAsync<QuestionRoute>();
        game.Question.Activity!.Call.Complete(false);
        await continued.Task.WaitAsync(TestTimeout);
        try
        {
            Assert.False(flow.WaitAsync().IsCompleted);
            Assert.Equal(0, game.Home.Disposed);
        }
        finally
        {
            release.TrySetResult(true);
        }
        await flow.WaitAsync().WaitAsync(TestTimeout);
        Assert.Equal("Applied", game.Home.View.Text);
    }

    [Fact]
    public async Task Work_can_render_a_retained_inactive_view_without_a_library_execution_context ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        ScreenActivityContext initial = game.Home.Activity!;
        ScreenWork flow = initial.StartWork(async work =>
        {
            SynchronizationContext? execution = SynchronizationContext.Current;
            Task call = work.Navigation.InvokeAsync(new DetailsRoute());
            await game.WaitForTopAsync<DetailsRoute>();
            Assert.True(initial.CancellationToken.IsCancellationRequested);
            Assert.False(work.CancellationToken.IsCancellationRequested);
            Assert.Equal(0, game.Home.Disposed);
            Assert.False(game.Home.View.Presentation.InputEnabled);
            Assert.Same(execution, SynchronizationContext.Current);
            game.Home.View.Text = "Updated behind the popup";
            await game.Host.Client.BackAsync(game.Host.Root);
            await call;
            Assert.Same(execution, SynchronizationContext.Current);
        });
        await flow.WaitAsync().WaitAsync(TestTimeout);
        Assert.Equal("Updated behind the popup", game.Home.View.Text);
    }

    [Fact]
    public async Task Work_wait_rejects_lifecycle_and_self_waits ()
    {
        await using Harness game = new();
        ScreenWork? flow = null;
        game.Home.OnActivate = activity =>
        {
            flow = activity.StartWork(context =>
            {
                Assert.Throws<InvalidOperationException>(() =>
                {
                    _ = flow!.WaitAsync();
                });
                return default;
            });
            Assert.Throws<InvalidOperationException>(() =>
            {
                _ = flow.WaitAsync();
            });
        };
        await game.Host.StartAsync(new HomeRoute("home"));
        await flow!.WaitAsync().WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task Canceling_a_wait_does_not_cancel_the_owned_work ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        TaskCompletionSource<bool> release = Signal<bool>();
        ScreenWork flow = game.Home.Activity!.StartWork(async work =>
        {
            await release.Task;
            Assert.False(work.CancellationToken.IsCancellationRequested);
        });
        using CancellationTokenSource cancellation = new();
        Task wait = flow.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait.WaitAsync(TestTimeout));
            Assert.False(flow.WaitAsync().IsCompleted);
        }
        finally
        {
            release.TrySetResult(true);
        }
        await flow.WaitAsync().WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task Canceling_pending_work_completes_without_running_the_callback ()
    {
        await using Harness game = new();
        ScreenWork? flow = null;
        bool ran = false;
        game.Home.OnActivate = activity =>
        {
            flow = activity.StartWork(_ =>
            {
                ran = true;
                return default;
            });
            flow.Cancel();
        };
        await game.Host.StartAsync(new HomeRoute("home"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flow!.WaitAsync().WaitAsync(TestTimeout));
        Assert.False(ran);
    }

    [Fact]
    public async Task Waiting_work_prevents_single_instance_rebinding_without_canceling_the_original_visit ()
    {
        await using Harness game = new(homeLower: LowerPresentationPolicy.HideAndRetain);
        await game.Host.StartAsync(new HomeRoute("first"));
        ScreenActivityContext original = game.Home.Activity!;
        TaskCompletionSource<CancellationToken> entered = Signal<CancellationToken>();
        TaskCompletionSource<bool> release = Signal<bool>();
        original.StartWork(async work =>
        {
            entered.SetResult(work.CancellationToken);
            await release.Task;
            Assert.Equal(new HomeRoute("first"), game.Home.Route);
        });
        CancellationToken workToken = await entered.Task.WaitAsync(TestTimeout);
        try
        {
            Assert.Throws<NavigationConfigurationException>(() => original.Navigation.Push(new HomeRoute("second")));
            Assert.False(original.CancellationToken.IsCancellationRequested);
            Assert.False(workToken.IsCancellationRequested);
            Assert.Single(game.Host.State.Current.GetRegion(game.Host.Root).Entries);
            Assert.Single(game.Home.Preparations);
            Assert.Equal(1, game.Home.Initialized);
            Assert.Equal(0, game.Home.Disposed);
        }
        finally
        {
            release.TrySetResult(true);
        }
    }

    [Fact]
    public async Task Cancellation_during_a_details_transition_waits_for_its_reservation_and_closes_the_call ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        using CancellationTokenSource cancellation = new();
        Task<bool> answer = game.Host.Client.InvokeAsync(game.Host.Root, new QuestionRoute(1), cancellation.Token);
        await game.WaitForTopAsync<QuestionRoute>();
        TaskCompletionSource<bool> preparing = Signal<bool>();
        TaskCompletionSource<bool> release = Signal<bool>();
        game.Details.OnPrepare = async () =>
        {
            preparing.SetResult(true);
            await release.Task;
        };
        Task<NavigationResult> pushing = game.Question.Activity!.Navigation.Push(new DetailsRoute()).WaitAsync();
        await preparing.Task.WaitAsync(TestTimeout);
        cancellation.Cancel();
        Assert.False(answer.IsCompleted);
        release.SetResult(true);
        await pushing.WaitAsync(TestTimeout);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => answer.WaitAsync(TestTimeout));
        Assert.Single(game.Host.State.Current.GetRegion(game.Host.Root).Entries);
        Assert.Equal(1, game.Details.Disposed);
        Assert.True(game.Home.View.Presentation.InputEnabled);
    }

    [Fact]
    public async Task Invalid_back_settings_do_not_stop_or_change_the_current_screen ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        await game.Home.Activity!.Navigation.PushAsync(new DetailsRoute());
        ScreenActivityContext current = game.Details.Activity!;
        Assert.Throws<NavigationConfigurationException>(() => current.Navigation.Back(new BackOptions { Preparation = (ScreenPreparationMode)100 }));
        Assert.False(current.CancellationToken.IsCancellationRequested);
        Assert.Equal(2, game.Host.State.Current.GetRegion(game.Host.Root).Entries.Count);
    }

    [Theory]
    [InlineData(false, ScreenEnterAnimationMode.WhenShown, 1)]
    [InlineData(false, ScreenEnterAnimationMode.Always, 2)]
    [InlineData(false, ScreenEnterAnimationMode.Skip, 1)]
    [InlineData(true, ScreenEnterAnimationMode.WhenShown, 2)]
    [InlineData(true, ScreenEnterAnimationMode.Always, 2)]
    [InlineData(true, ScreenEnterAnimationMode.Skip, 1)]
    public async Task Back_controls_enter_animation_independently_of_preparation (bool hide, ScreenEnterAnimationMode animation, int enters)
    {
        await using Harness game = new(detailsLower: hide ? LowerPresentationPolicy.HideAndRetain : LowerPresentationPolicy.BlockInput);
        await game.Host.StartAsync(new HomeRoute("chapter"));
        ScreenActivityContext first = game.Home.Activity!;
        Assert.True(first.IsFirstActivation);
        Assert.Equal(ScreenActivationReason.Entry, first.Reason);
        await first.Navigation.PushAsync(new DetailsRoute());
        await game.Details.Activity!.Navigation.BackAsync(new BackOptions { EnterAnimation = animation });
        Assert.Equal(enters, game.Home.Animator.Entered);
        Assert.Equal(ScreenAnimationState.Foreground, game.Home.Animator.State);
        Assert.Single(game.Home.Preparations);
        Assert.Equal(first.EntryId, game.Home.Activity!.EntryId);
        Assert.False(game.Home.Activity.IsFirstActivation);
        Assert.Equal(ScreenActivationReason.HistoryReturn, game.Home.Activity.Reason);
        Assert.Null(game.Home.Activity.PreparationReason);
        Assert.Equal(new HomeRoute("chapter"), game.Home.Route);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reentry_prepares_latest_state_without_recreating_the_instance (bool configured)
    {
        await using Harness game = new(historyReturn: configured ? new()
        {
            Preparation = ScreenPreparationMode.Always
        } : null);
        await game.Host.StartAsync(new HomeRoute("chapter"));
        HomeScreen original = game.Home;
        await original.Activity!.Navigation.PushAsync(new DetailsRoute());
        original.Selection = 42;
        await game.Details.Activity!.Navigation.BackAsync(configured ? null : new BackOptions { Preparation = ScreenPreparationMode.Always });
        Assert.Same(original, game.Home);
        Assert.Equal(1, original.Initialized);
        Assert.Equal(0, original.Disposed);
        Assert.Equal(42, original.Selection);
        Assert.Equal(ScreenPreparationReason.Reentry, original.Preparations.Last().Reason);
        Assert.Equal(42, original.Preparations.Last().SavedState);
        Assert.Equal(ScreenPreparationReason.Reentry, original.Activity!.PreparationReason);
    }

    [Fact]
    public async Task Back_can_override_the_definition_to_keep_the_prepared_display ()
    {
        await using Harness game = new(historyReturn: new()
        {
            Preparation = ScreenPreparationMode.Always
        });
        await game.Host.StartAsync(new HomeRoute("home"));
        await game.Home.Activity!.Navigation.PushAsync(new DetailsRoute());
        await game.Details.Activity!.Navigation.BackAsync(new BackOptions { Preparation = ScreenPreparationMode.WhenRequired });
        Assert.Single(game.Home.Preparations);
    }

    [Fact]
    public async Task Reentry_preserves_preparation_dependencies_used_by_waiting_work ()
    {
        await using Harness game = new(historyReturn: new()
        {
            Preparation = ScreenPreparationMode.Always
        });
        await game.Host.StartAsync(new HomeRoute("home"));
        OwnedResource original = game.Home.PreparationResource!;
        TaskCompletionSource<bool> answered = Signal<bool>();
        game.Home.Activity!.StartWork(async work =>
        {
            await work.Navigation.InvokeAsync(new QuestionRoute(1));
            Assert.Equal(0, original.Disposed);
            Assert.NotSame(original, game.Home.PreparationResource);
            answered.SetResult(true);
        });
        await game.WaitForTopAsync<QuestionRoute>();
        game.Question.Activity!.Call.Complete(true);
        await answered.Task.WaitAsync(TestTimeout);
        await game.Host.ShutdownAsync();
        Assert.Equal(1, original.Disposed);
    }

    [Fact]
    public async Task A_posted_reset_runs_after_activation_and_can_end_its_source ()
    {
        await using Harness game = new();
        game.Home.OnActivate = activity => activity.Navigation.PostReset(new EndRoute());
        await game.Host.StartAsync(new HomeRoute("home"));
        await game.WaitForTopAsync<EndRoute>();
        await game.Host.ShutdownAsync();
        Assert.Equal(1, game.Home.Disposed);
    }

    [Fact]
    public async Task A_failed_activation_discards_posted_navigation ()
    {
        await using Harness game = new();
        game.Home.OnActivate = activity =>
        {
            activity.Navigation.PostPush(new DetailsRoute());
            throw new InvalidOperationException("Cannot activate.");
        };
        await Assert.ThrowsAsync<NavigationException>(() => game.Host.Start(new HomeRoute("home")).WaitAsync());
        await game.Host.ShutdownAsync();
        Assert.Equal(0, game.Details.Initialized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invoke_waits_for_answer_cleanup_and_caller_resumption (bool useDi)
    {
        await using Harness game = new(useDi);
        await game.Host.StartAsync(new HomeRoute("inventory"));
        ScreenActivityContext original = game.Home.Activity!;
        TaskCompletionSource<bool> answer = Signal<bool>();
        _ = original.StartWork(async context =>
        {
            bool value = await context.Navigation.InvokeAsync(new QuestionRoute(10));
            Assert.Equal(1, game.Question.Terminated);
            Assert.Equal(1, game.Question.Disposed);
            Assert.NotSame(original, game.Home.Activity);
            Assert.True(game.Home.View.Presentation.InputEnabled);
            answer.SetResult(value);
        });
        await game.WaitForTopAsync<QuestionRoute>();
        Assert.True(original.CancellationToken.IsCancellationRequested);
        Assert.False(answer.Task.IsCompleted);
        Assert.Equal(10, game.Question.Input);

        game.Question.Activity!.Call.Complete(false);

        Assert.False(await answer.Task.WaitAsync(TestTimeout));
        Assert.Equal(new HomeRoute("inventory"), game.Home.Route);
        Assert.Single(game.Host.State.Current.GetRegion(game.Host.Root).Entries);
    }

    [Fact]
    public async Task Details_back_keeps_the_call_and_invalidates_old_reply_permissions ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        Task<bool> answer = game.Host.Client.InvokeAsync(game.Host.Root, new QuestionRoute(1));
        await game.WaitForTopAsync<QuestionRoute>();
        ScreenActivityContext<bool> old = game.Question.Activity!;
        await old.Navigation.PushAsync(new DetailsRoute());
        Assert.False(answer.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => old.Call.Complete(true));

        await game.Details.Activity!.Navigation.BackAsync();

        Assert.NotSame(old, game.Question.Activity);
        Assert.Equal(1, game.Question.Prepared);
        game.Question.Activity!.Call.Complete(true);
        Assert.True(await answer.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task Nested_calls_return_each_answer_to_their_own_work ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        Task<bool> outer = game.Host.Client.InvokeAsync(game.Host.Root, new QuestionRoute(1));
        await game.WaitForTopAsync<QuestionRoute>();
        _ = game.Question.Activity!.StartWork(async (ScreenWorkContext<bool> context) =>
        {
            string? name = await context.Navigation.InvokeAsync(new NameRoute());
            Assert.Equal("player", name);
            context.Call.Complete(true);
        });
        await game.WaitForTopAsync<NameRoute>();
        Assert.False(outer.IsCompleted);
        game.Name.Activity!.Call.Complete("player");
        Assert.True(await outer.WaitAsync(TestTimeout));
        Assert.Equal(1, game.Name.Disposed);
        Assert.Equal(1, game.Question.Disposed);
    }

    [Fact]
    public async Task Typed_replace_and_reset_keep_the_outer_call ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        Task<bool> answer = game.Host.Client.InvokeAsync(game.Host.Root, new QuestionRoute(1));
        await game.WaitForTopAsync<QuestionRoute>();

        await game.Question.Activity!.Call.Replace(new QuestionRoute(2)).WaitAsync();
        Assert.False(answer.IsCompleted);
        Assert.Equal(2, game.Question.Input);
        await game.Question.Activity!.Navigation.PushAsync(new DetailsRoute());
        await game.Host.Client.BackAsync(game.Host.Root);
        await game.Question.Activity!.Call.Reset(new QuestionRoute(3)).WaitAsync();
        Assert.False(answer.IsCompleted);
        Assert.Equal(2, game.Host.State.Current.GetRegion(game.Host.Root).Entries.Count);
        game.Question.Activity!.Call.Complete(true);
        Assert.True(await answer.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task Back_cancels_without_fabricating_a_false_answer ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        Task<bool> answer = game.Host.Client.InvokeAsync(game.Host.Root, new QuestionRoute(1));
        await game.WaitForTopAsync<QuestionRoute>();
        await game.Question.Activity!.Navigation.BackAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => answer.WaitAsync(TestTimeout));
        Assert.True(game.Home.View.Presentation.InputEnabled);
    }

    [Fact]
    public async Task Cancellation_closes_the_entire_call_including_details ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        using CancellationTokenSource cancellation = new();
        Task<bool> answer = game.Host.Client.InvokeAsync(game.Host.Root, new QuestionRoute(1), cancellation.Token);
        await game.WaitForTopAsync<QuestionRoute>();
        await game.Question.Activity!.Navigation.PushAsync(new DetailsRoute());

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => answer.WaitAsync(TestTimeout));
        Assert.Single(game.Host.State.Current.GetRegion(game.Host.Root).Entries);
        Assert.Equal(1, game.Question.Disposed);
        Assert.Equal(1, game.Details.Disposed);
    }

    [Fact]
    public async Task Reset_releases_child_waits_before_waiting_for_owner_work ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        _ = game.Home.Activity!.StartWork(async context =>
        {
            await context.Navigation.InvokeAsync(new QuestionRoute(1));
            throw new InvalidOperationException("Removed owner must not receive an answer.");
        });
        await game.WaitForTopAsync<QuestionRoute>();

        await game.Question.Activity!.Navigation.GetRegionNavigation(RegionTarget.Root).ResetAsync(new EndRoute());

        await game.Host.ShutdownAsync();
        Assert.Equal(1, game.Home.Disposed);
        Assert.Equal(1, game.Question.Disposed);
        Assert.Equal(1, game.Home.Activated);
    }

    [Fact]
    public async Task Work_registered_during_activation_starts_after_success_and_does_not_restart_on_return ()
    {
        await using Harness game = new();
        int started = 0;
        TaskCompletionSource<bool> answer = Signal<bool>();
        game.Home.OnActivate = activity =>
        {
            if (activity.IsFirstActivation)
            {
                activity.StartWork(async work =>
                {
                    Assert.True(game.Home.View.Presentation.InputEnabled);
                    Assert.Single(game.CompletedOperations);
                    started++;
                    answer.SetResult(await work.Navigation.InvokeAsync(new QuestionRoute(1)));
                });
            }
        };
        await game.Host.StartAsync(new HomeRoute("home"));
        await game.WaitForTopAsync<QuestionRoute>();
        game.Question.Activity!.Call.Complete(true);
        Assert.True(await answer.Task.WaitAsync(TestTimeout));
        Assert.Equal(1, started);
        Assert.Equal(2, game.Home.Activated);
    }

    [Fact]
    public async Task Dismiss_cancels_after_restoring_the_caller ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        Task<bool> answer = game.Host.Client.InvokeAsync(game.Host.Root, new QuestionRoute(1));
        await game.WaitForTopAsync<QuestionRoute>();
        game.Question.Activity!.Call.Dismiss();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => answer.WaitAsync(TestTimeout));
        Assert.Equal(2, game.Home.Activated);
    }

    [Fact]
    public async Task Shutdown_cancels_waits_and_awaits_owned_work_before_disposal ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        TaskCompletionSource<bool> stopped = Signal<bool>();
        _ = game.Home.Activity!.StartWork(async context =>
        {
            try
            {
                await context.Navigation.InvokeAsync(new QuestionRoute(1));
            }
            finally
            {
                Assert.Equal(0, game.Home.Disposed);
                stopped.SetResult(true);
            }
        });
        await game.WaitForTopAsync<QuestionRoute>();
        await game.Host.ShutdownAsync().AsTask().WaitAsync(TestTimeout);
        Assert.True(await stopped.Task.WaitAsync(TestTimeout));
        Assert.Equal(1, game.Home.Disposed);
    }

    [Fact]
    public async Task Failure_to_resume_the_caller_faults_the_call_without_undoing_the_answer ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        game.Home.OnActivate = _ => throw new InvalidOperationException("Resume failed.");
        Task<bool> call = game.Host.Client.InvokeAsync(game.Host.Root, new QuestionRoute(1));
        await game.WaitForTopAsync<QuestionRoute>();
        game.Question.Activity!.Call.Complete(false);

        ScreenCallException failure = await Assert.ThrowsAsync<ScreenCallException>(() => call.WaitAsync(TestTimeout));

        Assert.True(failure.AnswerCommitted);
        Assert.Equal(ScreenCallFailureStage.Returning, failure.Stage);
        Assert.Single(game.Host.State.Current.GetRegion(game.Host.Root).Entries);
        Assert.False(game.Home.View.Presentation.InputEnabled);
    }

    [Fact]
    public async Task A_failed_activation_does_not_start_its_registered_work ()
    {
        await using Harness game = new();
        bool ran = false;
        ScreenWork? pending = null;
        game.Home.OnActivate = activity =>
        {
            pending = activity.StartWork(_ =>
            {
                ran = true;
                return default;
            });
            throw new InvalidOperationException("Activation failed.");
        };
        await Assert.ThrowsAsync<NavigationException>(() => game.Host.Start(new HomeRoute("home")).WaitAsync());
        Assert.NotNull(pending);
        await game.Host.ShutdownAsync();
        Assert.False(ran);
    }

    [Fact]
    public async Task Host_navigation_cannot_bypass_the_lifecycle_guard ()
    {
        await using Harness game = new();
        game.Home.OnActivate = _ => game.Host.Client.Push(game.Host.Root, Destination.For(new DetailsRoute()));
        await Assert.ThrowsAsync<NavigationException>(() => game.Host.Start(new HomeRoute("home")).WaitAsync());
        Assert.Single(game.Host.State.Current.GetRegion(game.Host.Root).Entries);
    }

    [Fact]
    public async Task Opening_cancellation_cleans_partial_construction_and_keeps_the_original_history ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        TaskCompletionSource<bool> preparing = Signal<bool>();
        game.PrepareQuestion = async token =>
        {
            preparing.SetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        using CancellationTokenSource cancellation = new();
        Task<bool> answer = game.Host.Client.InvokeAsync(game.Host.Root, new QuestionRoute(1), cancellation.Token);
        await preparing.Task.WaitAsync(TestTimeout);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => answer.WaitAsync(TestTimeout));
        Assert.Single(game.Host.State.Current.GetRegion(game.Host.Root).Entries);
        Assert.Equal(1, game.Question.Disposed);
        Assert.True(game.Home.View.Presentation.InputEnabled);
    }

    [Fact]
    public async Task Noncooperative_work_keeps_its_dependencies_alive_until_it_stops ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        TaskCompletionSource<bool> entered = Signal<bool>();
        TaskCompletionSource<bool> release = Signal<bool>();
        _ = game.Home.Activity!.StartWork(async _ =>
        {
            entered.SetResult(true);
            await release.Task;
            Assert.Equal(0, game.Home.Disposed);
        });
        await entered.Task.WaitAsync(TestTimeout);
        await game.Host.Client.ResetAsync(game.Host.Root, Destination.For(new EndRoute()));
        Task shutdown = game.Host.ShutdownAsync().AsTask();
        Assert.False(shutdown.IsCompleted);
        Assert.Equal(0, game.Home.Disposed);
        release.SetResult(true);
        await shutdown.WaitAsync(TestTimeout);
        Assert.Equal(1, game.Home.Disposed);
    }

    [Fact]
    public async Task A_work_cannot_wait_for_its_own_removal ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        TaskCompletionSource<bool> rejected = Signal<bool>();
        _ = game.Home.Activity!.StartWork(async context =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.Navigation.Reset(new EndRoute()).WaitAsync());
            rejected.SetResult(true);
        });
        Assert.True(await rejected.Task.WaitAsync(TestTimeout));
        await game.WaitForTopAsync<EndRoute>();
        await game.Host.ShutdownAsync();
        Assert.Equal(1, game.Home.Disposed);
    }

    [Fact]
    public async Task A_host_call_restores_the_same_history_entry_after_its_instance_was_recreated ()
    {
        await using Harness game = new(releaseCaller: true);
        await game.Host.StartAsync(new HomeRoute("home"));
        HomeScreen previous = game.Home;
        NavigationEntryId entry = previous.Activity!.EntryId;
        Task<bool> call = game.Host.Client.InvokeAsync(game.Host.Root, new QuestionRoute(1));
        await game.WaitForTopAsync<QuestionRoute>();
        Assert.Equal(1, previous.Disposed);
        game.Question.Activity!.Call.Complete(true);
        Assert.True(await call.WaitAsync(TestTimeout));
        Assert.NotSame(previous, game.Home);
        Assert.Equal(entry, game.Home.Activity!.EntryId);
        Assert.False(game.Home.Activity.IsFirstActivation);
        Assert.Equal(ScreenActivationReason.HistoryReturn, game.Home.Activity.Reason);
    }

    [Fact]
    public async Task Invoke_pins_the_caller_even_when_the_destination_would_normally_release_it ()
    {
        await using Harness game = new(releaseCaller: true);
        await game.Host.StartAsync(new HomeRoute("home"));
        HomeScreen previous = game.Home;
        TaskCompletionSource<bool> answer = Signal<bool>();
        _ = previous.Activity!.StartWork(async context => answer.SetResult(await context.Navigation.InvokeAsync(new QuestionRoute(1))));
        await game.WaitForTopAsync<QuestionRoute>();
        Assert.Equal(0, previous.Disposed);
        game.Question.Activity!.Call.Complete(true);
        Assert.True(await answer.Task.WaitAsync(TestTimeout));
        Assert.Same(previous, game.Home);
    }

    private static TaskCompletionSource<T> Signal<T> () => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Activity_can_invoke_directly_and_receive_an_answer_after_resumption (bool useDi)
    {
        await using Harness game = new(useDi, releaseCaller: true);
        await game.Host.StartAsync(new HomeRoute("home"));
        HomeScreen owner = game.Home;
        ScreenActivityContext original = owner.Activity!;
        Task<bool> answer = original.Navigation.InvokeAsync(new QuestionRoute(8));
        await game.WaitForTopAsync<QuestionRoute>();
        Assert.True(original.CancellationToken.IsCancellationRequested);
        Assert.False(answer.IsCompleted);
        Assert.Equal(0, owner.Disposed);
        game.Question.Activity!.Call.Complete(false);
        Assert.False(await answer.WaitAsync(TestTimeout));
        Assert.Same(owner, game.Home);
        Assert.True(owner.View.Presentation.InputEnabled);
        Assert.Equal(1, game.Question.Disposed);
        await Assert.ThrowsAsync<NavigationException>(() => original.Navigation.PushAsync(new DetailsRoute()));
        await owner.Activity!.Navigation.PushAsync(new DetailsRoute());
    }

    [Fact]
    public async Task Removing_the_owner_cancels_a_direct_call_and_does_not_resume_it ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        Task<bool> answer = game.Home.Activity!.Navigation.InvokeAsync(new QuestionRoute(8));
        await game.WaitForTopAsync<QuestionRoute>();
        await game.Host.Client.ResetAsync(game.Host.Root, Destination.For(new EndRoute()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => answer.WaitAsync(TestTimeout));
        Assert.Equal(1, game.Home.Activated);
    }

    [Fact]
    public async Task Reloading_an_answering_screen_keeps_the_pending_call_and_rejects_the_old_reply ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        Task<bool> answer = game.Home.Activity!.Navigation.InvokeAsync(new QuestionRoute(8));
        await game.WaitForTopAsync<QuestionRoute>();
        AnswerScreen<QuestionRoute, bool> old = game.Question;
        NavigationEntryId entry = old.Activity!.EntryId;
        await old.Activity.Navigation.ReloadAsync();
        Assert.Equal(entry, game.Question.Activity!.EntryId);
        Assert.Equal(8, game.Question.Input);
        Assert.NotSame(old, game.Question);
        Assert.Equal(1, old.Disposed);
        Assert.False(answer.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => old.Activity.Call.Complete(false));
        game.Question.Activity.Call.Complete(true);
        Assert.True(await answer.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task A_pending_direct_call_prevents_rebinding_its_owner_before_any_state_changes ()
    {
        await using Harness game = new();
        await game.Host.StartAsync(new HomeRoute("home"));
        Task<bool> answer = game.Home.Activity!.Navigation.InvokeAsync(new QuestionRoute(8));
        await game.WaitForTopAsync<QuestionRoute>();
        long revision = game.Host.State.Current.Revision;
        await Assert.ThrowsAsync<NavigationConfigurationException>(() => game.Host.Client.PushAsync(game.Host.Root,
            Destination.For(new HomeRoute("another"))));
        Assert.Equal(revision, game.Host.State.Current.Revision);
        Assert.Equal(0, game.Home.Disposed);
        game.Question.Activity!.Call.Complete(true);
        Assert.True(await answer.WaitAsync(TestTimeout));
    }

    private sealed record HomeRoute (string Name) : Route;
    private sealed record DetailsRoute : Route;
    private sealed record EndRoute : Route;
    private sealed record QuestionRoute (int Cost) : Route<bool>;
    private sealed record NameRoute : Route<string?>;

    private sealed class Harness : IAsyncDisposable
    {
        private readonly object sync = new();
        private TaskCompletionSource<bool> changed = Signal<bool>();
        private NavigationRoute? completedTop;
        public HomeScreen Home { get; private set; } = new();
        public Func<CancellationToken, ValueTask>? PrepareQuestion
        {
            get; set;
        }
        public OrdinaryScreen<DetailsRoute> Details { get; } = new();
        public OrdinaryScreen<EndRoute> End { get; } = new();
        public AnswerScreen<QuestionRoute, bool> Question { get; private set; } = new();
        public AnswerScreen<NameRoute, string?> Name { get; private set; } = new();
        public NavigationHost Host
        {
            get;
        }
        public List<NavigationResult> CompletedOperations { get; } = new();

        public Harness (bool useDi = false, bool releaseCaller = false, LowerPresentationPolicy? detailsLower = null, ScreenHistoryReturnOptions? historyReturn = null, LowerPresentationPolicy? homeLower = null)
        {
            RegionDefinitionId rootId = new("game");
            NavigationDefinition definition = NavigationDefinition.Build(rootId, RegionCompositionMode.Layered, root =>
            {
                root.AddRoute<HomeRoute>(route =>
                {
                    Configure(route);
                    route.LowerPresentationPolicy = homeLower ?? LowerPresentationPolicy.BlockInput;
                });
                root.AddRoute<DetailsRoute>(route =>
                {
                    Configure(route);
                    route.LowerPresentationPolicy = detailsLower ?? LowerPresentationPolicy.BlockInput;
                });
                root.AddRoute<EndRoute>(Configure);
                root.AddRoute<QuestionRoute>(route =>
                {
                    Configure(route);
                    if (releaseCaller)
                    {
                        route.LowerPresentationPolicy = LowerPresentationPolicy.HideAndRelease;
                    }
                });
                root.AddRoute<NameRoute>(Configure);
            });
            ScreenCatalog catalog = ScreenCatalog.Build(definition, catalog => catalog.RegisterScreens(rootId, screens =>
            {
                int homes = 0;
                screens.RegisterScreen(new ScreenDefinition<HomeRoute>((creation, _) =>
                {
                    if (homes++ > 0)
                    {
                        Home = new();
                    }
                    creation.ConnectPresentation(new ScreenPresentationBinding(new[] { Home.View }, Home.Animator));
                    return new ValueTask<IScreenLifecycleHandler<HomeRoute>>(creation.Lifetime.CreateOwned(() => Home));
                })
                {
                    HistoryReturn = historyReturn ?? new()
                });
                screens.RegisterScreen(Define(Details));
                screens.RegisterScreen(Define(End));
                screens.RegisterScreen(new ScreenDefinition<QuestionRoute, bool>(async (creation, token) =>
                {
                    Question = new();
                    creation.ConnectPresentation(new ScreenPresentationBinding(new[] { Question.View }));
                    IScreenLifecycleHandler<QuestionRoute, bool> handler;
                    if (useDi)
                    {
                        handler = creation.CreateScope(services =>
                        {
                            services.AddScoped(_ => Question);
                            services.AddScreenLifecycleHandler<AnswerScreen<QuestionRoute, bool>>();
                        });
                    }
                    else
                    {
                        handler = creation.Lifetime.CreateOwned(() => Question);
                    }
                    if (PrepareQuestion is not null)
                    {
                        await PrepareQuestion(token);
                    }
                    return handler;
                }));
                screens.RegisterScreen(new ScreenDefinition<NameRoute, string?>((creation, _) =>
                {
                    Name = new();
                    creation.ConnectPresentation(new ScreenPresentationBinding(new[] { Name.View }));
                    return new ValueTask<IScreenLifecycleHandler<NameRoute, string?>>(creation.Lifetime.CreateOwned(() => Name));
                }));
            }));
            Host = NavigationHost.Create(catalog, new NavigationHostOptions
            {
                OperationCompleted = result =>
                {
                    lock (sync)
                    {
                        CompletedOperations.Add(result);
                        NavigationState snapshot = result.FinalSnapshot;
                        NavigationEntryId top = snapshot.GetRegion(snapshot.RootRegionInstanceId).Entries.Last();
                        completedTop = snapshot.GetEntry(top).Route;
                        TaskCompletionSource<bool> previous = changed;
                        changed = Signal<bool>();
                        previous.TrySetResult(true);
                    }
                }
            });
        }

        private static void Configure<T> (RouteDefinitionBuilder<T> route) where T : NavigationRoute
        {
            route.AllowedEntryOperations = RouteEntryOperations.Push | RouteEntryOperations.Replace | RouteEntryOperations.Reset;
            route.LowerPresentationPolicy = LowerPresentationPolicy.BlockInput;
        }

        private static ScreenDefinition<T> Define<T> (OrdinaryScreen<T> screen) where T : Route
            => new((creation, _) =>
            {
                creation.ConnectPresentation(new ScreenPresentationBinding(new[] { screen.View }));
                return new ValueTask<IScreenLifecycleHandler<T>>(creation.Lifetime.CreateOwned(() => screen));
            });

        public async Task WaitForTopAsync<T> () where T : NavigationRoute
        {
            while (true)
            {
                Task wait;
                lock (sync)
                {
                    if (completedTop is T)
                    {
                        return;
                    }
                    wait = changed.Task;
                }
                await wait.WaitAsync(TestTimeout);
            }
        }

        public ValueTask DisposeAsync () => Host.ShutdownAsync();
    }

    private class OrdinaryScreen<T> : IScreenLifecycleHandler<T>, IScreenStateCapture, IDisposable where T : Route
    {
        public View View { get; } = new();
        public ScreenActivityContext? Activity
        {
            get; private set;
        }
        public T? Route
        {
            get; private set;
        }
        public int Activated
        {
            get; private set;
        }
        public int Disposed
        {
            get; private set;
        }
        public Action<ScreenActivityContext>? OnActivate
        {
            get; set;
        }
        public Func<ValueTask>? OnPrepare
        {
            get; set;
        }
        public Func<ValueTask>? OnTerminate
        {
            get; set;
        }
        public int Initialized
        {
            get; private set;
        }
        public int Selection
        {
            get; set;
        }
        public Animator Animator { get; } = new();
        public List<ScreenPreparationContext> Preparations { get; } = new();
        public OwnedResource? PreparationResource
        {
            get; private set;
        }
        public object CaptureState () => Selection;
        public ValueTask InitializeAsync (CancellationToken cancellationToken)
        {
            Initialized++;
            return default;
        }
        public async ValueTask PrepareAsync (T route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            Preparations.Add(preparation);
            PreparationResource = preparation.Lifetime.CreateOwned(() => new OwnedResource());
            Selection = preparation.SavedState is int saved ? saved : 0;
            if (OnPrepare is not null)
            {
                await OnPrepare();
            }
        }
        public ValueTask ActivateAsync (T route, ScreenActivityContext activity)
        {
            Activity = activity;
            Route = route;
            Activated++;
            OnActivate?.Invoke(activity);
            return default;
        }
        public ValueTask DeactivateAsync () => default;
        public ValueTask TerminateAsync () => OnTerminate?.Invoke() ?? default;
        public void Dispose () => Disposed++;
    }

    private sealed class HomeScreen : OrdinaryScreen<HomeRoute>
    {
    }

    private sealed class OwnedResource : IDisposable
    {
        public int Disposed
        {
            get; private set;
        }
        public void Dispose () => Disposed++;
    }

    private sealed class Animator : IScreenAnimator
    {
        public int Entered
        {
            get; private set;
        }
        public ScreenAnimationState State
        {
            get; private set;
        }
        public void SetStateImmediately (ScreenAnimationState state) => State = state;
        public ValueTask PlayAsync (ScreenAnimation animation, CancellationToken cancellationToken)
        {
            if (animation.Kind == ScreenAnimationKind.Enter || animation.Kind == ScreenAnimationKind.Reveal)
            {
                Entered++;
            }
            return default;
        }
    }

    private sealed class AnswerScreen<TRoute, TResult> : IScreenLifecycleHandler<TRoute, TResult>, IDisposable where TRoute : Route<TResult>
    {
        public View View { get; } = new();
        public ScreenActivityContext<TResult>? Activity
        {
            get; private set;
        }
        public int Input
        {
            get; private set;
        }
        public int Prepared
        {
            get; private set;
        }
        public int Terminated
        {
            get; private set;
        }
        public int Disposed
        {
            get; private set;
        }
        public ValueTask InitializeAsync (CancellationToken cancellationToken) => default;
        public ValueTask PrepareAsync (TRoute route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            Prepared++;
            Input = route is QuestionRoute question ? question.Cost : 0;
            return default;
        }
        public ValueTask ActivateAsync (TRoute route, ScreenActivityContext<TResult> activity)
        {
            Activity = activity;
            return default;
        }
        public ValueTask DeactivateAsync () => default;
        public ValueTask TerminateAsync ()
        {
            Terminated++;
            return default;
        }
        public void Dispose () => Disposed++;
    }

    private sealed class View : IViewAdapter
    {
        private static readonly object Domain = new();
        public object Identity { get; } = new();
        public object OrderingDomain => Domain;
        public bool IsAlive => true;
        public string? Text
        {
            get; set;
        }
        public ViewPresentation Presentation { get; private set; } = new(false, false, 0);
        public event Action<string>? Lost
        {
            add
            {
            }
            remove
            {
            }
        }
        public void Validate (ViewPresentation presentation)
        {
        }
        public void Apply (ViewPresentation presentation) => Presentation = presentation;
    }
}
