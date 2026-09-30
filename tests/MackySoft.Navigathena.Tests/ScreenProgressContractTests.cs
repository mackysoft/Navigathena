using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MackySoft.Navigathena.Hosting;
using MackySoft.Navigathena.Integration;
using Xunit;

namespace MackySoft.Navigathena.Tests;

public sealed class ScreenProgressContractTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ProgressInput<Detail> DetailInput = new("test.detail");
    private static readonly ProgressInput<int> IntegerInput = new("test.count");
    private static readonly ProgressInput<double?> FractionInput = new("test.fraction");
    private static readonly ProgressDefinition<ReportState> Reports = ProgressDefinition.Create(() => new ReportState())
        .Reduce(DetailInput, (state, update) => state with { Detail = update })
        .Reduce(IntegerInput, (state, update) => state with { Integer = update })
        .Reduce(FractionInput, (state, update) => state with { Fraction = update })
        .Build();

    [Fact]
    public async Task Typed_inputs_with_the_same_name_keep_independent_values_in_the_display_state ()
    {
        ProgressInput<int> downloads = new("loading");
        ProgressInput<string> status = new("loading");
        ProgressDefinition<(int Count, string Status)> definition = ProgressDefinition.Create(() => (Count: 0, Status: "Waiting"))
            .Reduce(downloads, (state, update) => (update.Value, state.Status))
            .Reduce(status, (state, update) => (state.Count, update.Value))
            .Build();
        List<(int Count, string Status)> displayed = new();
        Handler handler = new()
        {
            Prepare = (_, context, _) =>
            {
                context.Progress.GetReporter(downloads).Report(4);
                context.Progress.GetReporter(status).Report("Retrying");
                context.Progress.GetReporter(downloads).Report(5);
                return default;
            }
        };
        await using NavigationHost host = CreateHost(ScreenInstancePolicy.Single, handler);
        await host.StartAsync(new Page(1), new NavigationOptions
        {
            Transition = NavigationTransition.Create(NavigationTransitionScope.Region, definition, (context, source, _) =>
            {
                context.ObserveProgress(source, displayed.Add);
                return new(new Receiver());
            })
        });

        Assert.Equal(new[] { (0, "Waiting"), (4, "Waiting"), (4, "Retrying"), (5, "Retrying") }, displayed);
    }

    [Fact]
    public async Task Each_transition_has_fresh_state_and_late_observers_receive_the_current_snapshot ()
    {
        ProgressInput<int> input = new("load");
        ProgressDefinition<int> definition = ProgressDefinition.From(input, () => -1);
        List<int> initial = new();
        List<List<int>> displayed = new();
        List<ProgressSource<int>> sources = new();
        IProgress<int>? previous = null;
        Handler handler = new()
        {
            Prepare = (route, context, _) =>
            {
                previous?.Report(999);
                previous = context.Progress.GetReporter(input);
                previous.Report(route.Number);
                return default;
            }
        };
        NavigationTransition transition = NavigationTransition.Create(NavigationTransitionScope.Region, definition, async (context, source, token) =>
        {
            initial.Add(source.Current);
            sources.Add(source);
            await context.Lifetime.AcquireAsync(new Acquisition
            {
                Acquire = (acquisition, _) =>
                {
                    acquisition.Progress.GetReporter(input).Report(10);
                    return default;
                }
            }, token);
            List<int> values = new();
            displayed.Add(values);
            context.ObserveProgress(source, values.Add);
            return new Receiver();
        });
        await using NavigationHost host = CreateHost(ScreenInstancePolicy.Single, handler);
        await host.StartAsync(new Page(1), new NavigationOptions { Transition = transition });
        await host.Client.PushAsync(host.Root, Destination.For(new Page(2)), new NavigationOptions { Transition = transition });
        previous!.Report(999);

        Assert.Equal(new[] { -1, -1 }, initial);
        Assert.Equal(new[] { 10, 1 }, displayed[0]);
        Assert.Equal(new[] { 10, 2 }, displayed[1]);
        Assert.Equal(1, sources[0].Current);
        Assert.Equal(2, sources[1].Current);
    }

    [Fact]
    public async Task A_display_cannot_subscribe_to_a_previous_operations_source ()
    {
        ProgressInput<int> input = new("load");
        ProgressDefinition<int> definition = ProgressDefinition.From(input, () => 0);
        ProgressSource<int>? previous = null;
        Handler handler = new();
        await using NavigationHost host = CreateHost(ScreenInstancePolicy.Single, handler);
        await host.StartAsync(new Page(1), new NavigationOptions
        {
            Transition = NavigationTransition.Create(NavigationTransitionScope.Region, definition, (_, source, _) =>
            {
                previous = source;
                return new(new Receiver());
            })
        });
        List<int> displayed = new();
        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => host.Client.PushAsync(host.Root, Destination.For(new Page(2)), new NavigationOptions
        {
            Transition = NavigationTransition.Create(NavigationTransitionScope.Region, definition, (context, _, _) =>
            {
                context.ObserveProgress(previous!, displayed.Add);
                return new(new Receiver());
            })
        }));

        Assert.False(failure.DestinationCommitted);
        Assert.Empty(displayed);
        Assert.Single(host.State.Current.Entries);
        Assert.True(handler.Active);
    }

    [Fact]
    public async Task Reentrant_observer_reports_are_delivered_in_order_to_every_observer ()
    {
        ProgressInput<int> input = new("count");
        IProgress<int>? reporter = null;
        List<int> first = new();
        List<int> second = new();
        Handler handler = new()
        {
            Prepare = (_, context, _) =>
            {
                reporter = context.Progress.GetReporter(input);
                reporter.Report(1);
                return default;
            }
        };
        await using NavigationHost host = CreateHost(ScreenInstancePolicy.Single, handler);
        await host.StartAsync(new Page(1), new NavigationOptions
        {
            Transition = NavigationTransition.Create(NavigationTransitionScope.Region, ProgressDefinition.From(input, () => 0), (context, source, _) =>
            {
                context.ObserveProgress(source, value =>
                {
                    first.Add(value);
                    if (value == 1)
                    {
                        reporter!.Report(2);
                    }
                });
                context.ObserveProgress(source, second.Add);
                return new(new Receiver());
            })
        });

        Assert.Equal(new[] { 0, 1, 2 }, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Concurrent_reports_are_reduced_without_lost_updates ()
    {
        ProgressInput<int> input = new("completed");
        ProgressDefinition<int> definition = ProgressDefinition.Create(() => 0)
            .Reduce(input, (state, update) => state + update.Value)
            .Build();
        List<int> displayed = new();
        Handler handler = new()
        {
            Prepare = async (_, context, _) =>
            {
                IProgress<int> reporter = context.Progress.GetReporter(input);
                await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
                {
                    for (int index = 0; index < 50; index++)
                    {
                        reporter.Report(1);
                    }
                })));
            }
        };
        await using NavigationHost host = CreateHost(ScreenInstancePolicy.Single, handler);
        await host.StartAsync(new Page(1), new NavigationOptions
        {
            Transition = NavigationTransition.Create(NavigationTransitionScope.Region, definition, (context, source, _) =>
            {
                context.ObserveProgress(source, displayed.Add);
                return new(new Receiver());
            })
        });

        Assert.Equal(Enumerable.Range(0, 201), displayed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Overlapping_acquisitions_require_explicit_composition (bool compose)
    {
        ProgressInput<int> input = new("acquired");
        ProgressDefinition<int> definition = compose
            ? ProgressDefinition.Create(() => 0).Reduce(input, (state, update) => state + update.Value).Build()
            : ProgressDefinition.From(input, () => 0);
        Gate firstAcquisition = new();
        Acquisition first = new()
        {
            Acquire = async (context, _) =>
            {
                context.Progress.GetReporter(input).Report(2);
                await firstAcquisition.HoldAsync();
            }
        };
        Acquisition second = new()
        {
            Acquire = (context, _) =>
            {
                context.Progress.GetReporter(input).Report(3);
                return default;
            }
        };
        Handler handler = new()
        {
            Prepare = async (_, context, token) =>
            {
                ValueTask<string> pending = context.Lifetime.AcquireAsync(first, token);
                try
                {
                    await firstAcquisition.Entered.WaitAsync(Timeout);
                    await context.Lifetime.AcquireAsync(second, token);
                }
                finally
                {
                    firstAcquisition.Open();
                    await pending;
                }
            }
        };
        List<int> displayed = new();
        Receiver effect = new();
        await using NavigationHost host = CreateHost(ScreenInstancePolicy.Single, handler);
        Task start = host.StartAsync(new Page(1), new NavigationOptions
        {
            Transition = NavigationTransition.Create(NavigationTransitionScope.Region, definition, (context, source, _) =>
            {
                context.ObserveProgress(source, displayed.Add);
                return new(effect);
            })
        });
        if (compose)
        {
            await start;
            Assert.Equal(new[] { 0, 2, 5 }, displayed);
        }
        else
        {
            NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => start);
            Assert.True(failure.DestinationCommitted);
            Assert.Equal(new[] { 0, 2 }, displayed);
        }
        Assert.True(effect.Settled);
        Assert.True(handler.Active);
        await host.ShutdownAsync();
        Assert.Equal(1, first.Releases);
        Assert.Equal(1, second.Releases);
    }

    [Fact]
    public async Task Reduction_failure_keeps_the_last_valid_snapshot_and_does_not_skip_settlement ()
    {
        ProgressInput<int> input = new("count");
        ProgressDefinition<int> definition = ProgressDefinition.Create(() => 0)
            .Reduce(input, (state, update) => update.Value < 0 ? throw new InvalidOperationException("Invalid count.") : state + update.Value)
            .Build();
        List<int> displayed = new();
        Handler handler = new()
        {
            Prepare = (_, context, _) =>
            {
                IProgress<int> reporter = context.Progress.GetReporter(input);
                reporter.Report(2);
                reporter.Report(-1);
                reporter.Report(3);
                return default;
            }
        };
        Receiver effect = new();
        await using NavigationHost host = CreateHost(ScreenInstancePolicy.Single, handler);
        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => host.StartAsync(new Page(1), new NavigationOptions
        {
            Transition = NavigationTransition.Create(NavigationTransitionScope.Region, definition, (context, source, _) =>
            {
                context.ObserveProgress(source, displayed.Add);
                return new(effect);
            })
        }));

        Assert.True(failure.DestinationCommitted);
        Assert.Equal(new[] { 0, 2, 5 }, displayed);
        Assert.True(effect.Settled);
        Assert.True(handler.Active);
    }

    [Fact]
    public async Task Failed_effect_construction_disconnects_its_display_before_releasing_its_resources ()
    {
        ProgressInput<int> input = new("transition.asset");
        List<int> displayed = new();
        Acquisition asset = new()
        {
            Acquire = (context, _) =>
            {
                context.Progress.GetReporter(input).Report(1);
                return default;
            },
            Release = progress =>
            {
                progress.GetReporter(input).Report(2);
                return default;
            }
        };
        Handler handler = new();
        await using NavigationHost host = CreateHost(ScreenInstancePolicy.Single, handler);
        await Assert.ThrowsAsync<NavigationException>(() => host.StartAsync(new Page(1), new NavigationOptions
        {
            Transition = NavigationTransition.Create(NavigationTransitionScope.Region, ProgressDefinition.From(input, () => 0), async (context, source, token) =>
            {
                context.ObserveProgress(source, displayed.Add);
                await context.Lifetime.AcquireAsync(asset, token);
                throw new InvalidOperationException("Effect construction failed.");
            })
        }));

        Assert.Equal(new[] { 0, 1 }, displayed);
        Assert.Equal(1, asset.Releases);
        Assert.Empty(host.State.Current.Entries);
        Assert.False(handler.Active);
    }

    [Fact]
    public async Task Construction_acquisition_and_repreparation_have_separate_work_and_ignore_late_reports ()
    {
        Receiver observer = new();
        Receiver effect = new();
        Handler handler = new();
        Acquisition acquisition = new();
        NavigationProgressReporter? construction = null;
        ScreenCatalog catalog = ScreenCatalog.Build(screens => screens.Register<Page>(
            RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.HideAndRetain,
            async (creation, token) =>
            {
                construction = creation.Progress;
                creation.Progress.GetReporter(DetailInput).Report(new Detail("create", 0));
                await creation.Lifetime.AcquireAsync(acquisition, token);
                return handler;
            }));
        NavigationOptions options = new()
        {
            Progress = observer,
            Transition = CreateTransition(effect, TransitionEndTiming.AfterPresentation, observer)
        };
        await using NavigationHost host = NavigationHost.Create(catalog);
        await host.StartAsync(new Page(1), options);
        NavigationProgressReporter firstPreparation = handler.Progress!;
        Assert.Equal(new[] { "create", "acquire", "prepare" }, observer.Values.Select(value => value.Value.Name));
        Assert.Equal(3, observer.Values.Select(value => value.WorkId).Distinct().Count());
        Assert.Single(observer.Values.Select(value => value.OperationId).Distinct());
        Assert.Single(observer.Values.Select(value => value.EntryId).Distinct());
        Assert.Equal(observer.Values, effect.Values);
        Assert.True(effect.Settled);

        construction!.GetReporter(DetailInput).Report(new Detail("late creation", 0));
        acquisition.Progress!.GetReporter(DetailInput).Report(new Detail("late acquisition", 0));
        firstPreparation.GetReporter(DetailInput).Report(new Detail("late preparation", 0));
        Assert.Equal(3, observer.Values.Count);

        await host.Client.PushAsync(host.Root, Destination.For(new Page(2)), new NavigationOptions
        {
            Progress = observer,
            Transition = CreateTransition(new Receiver(), TransitionEndTiming.AfterPresentation, observer)
        });
        ProgressUpdate<Detail> next = observer.Values.Last();
        Assert.Equal(new Detail("prepare", 2), next.Value);
        Assert.NotEqual(observer.Values[2].WorkId, next.WorkId);
        Assert.NotEqual(observer.Values[2].EntryId, next.EntryId);
        Assert.NotEqual(observer.Values[2].OperationId, next.OperationId);
        firstPreparation.GetReporter(DetailInput).Report(new Detail("stale preparation", 0));
        Assert.Equal(4, observer.Values.Count);
        Assert.Equal(3, effect.Values.Count);
        await host.ShutdownAsync();
        Assert.Equal(1, acquisition.Releases);
    }

    [Fact]
    public async Task Progress_receiver_failure_does_not_interrupt_other_receivers_or_preparation ()
    {
        Receiver observer = new()
        {
            ThrowOnSample = true
        };
        Receiver effect = new();
        Handler handler = new();
        await using NavigationHost host = NavigationHost.Create(ScreenCatalog.Build(screens =>
            screens.Register<Page>(RouteEntryOperations.Reset, LowerPresentationPolicy.Preserve, (_, _) => new(handler))));
        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => host.StartAsync(new Page(4), new NavigationOptions
        {
            Progress = observer,
            Transition = CreateTransition(effect, TransitionEndTiming.AfterPresentation, observer)
        }));
        Assert.True(failure.DestinationCommitted);
        Assert.Equal(new Detail("prepare", 4), Assert.Single(effect.Values).Value);
        Assert.True(handler.Active);
    }

    [Fact]
    public async Task Failed_acquisition_closes_its_reports_and_releases_partial_ownership_without_adding_history ()
    {
        Receiver observer = new();
        Acquisition acquisition = new()
        {
            Fail = true
        };
        await using NavigationHost host = NavigationHost.Create(ScreenCatalog.Build(screens =>
            screens.Register<Page>(RouteEntryOperations.Reset, LowerPresentationPolicy.Preserve, async (creation, token) =>
            {
                await creation.Lifetime.AcquireAsync(acquisition, token);
                return new Handler();
            })));
        await Assert.ThrowsAsync<NavigationException>(() => host.StartAsync(new Page(1), new NavigationOptions
        {
            Progress = observer,
            Transition = CreateTransition(new Receiver(), TransitionEndTiming.AfterPresentation, observer)
        }));
        Assert.Empty(host.State.Current.Entries);
        Assert.Equal(1, acquisition.Releases);
        Assert.Equal("acquire", Assert.Single(observer.Values).Value.Name);
        acquisition.Progress!.GetReporter(DetailInput).Report(new Detail("after failure", 0));
        Assert.Single(observer.Values);
    }

    [Fact]
    public async Task Initialization_opens_its_own_registration_period_and_keeps_resources_for_the_reused_instance ()
    {
        Receiver observer = new();
        Acquisition constructionResource = new();
        Acquisition initializationResource = new()
        {
            Release = progress =>
            {
                progress.GetReporter(DetailInput).Report(new Detail("shutdown release", 0));
                return default;
            }
        };
        OwnedResource owned = new();
        ScreenCreationContext? construction = null;
        ScreenInitializationContext? initialization = null;
        Handler handler = new()
        {
            Initialize = async (context, token) =>
            {
                initialization = context;
                Assert.Throws<InvalidOperationException>(() => construction!.Lifetime.CreateOwned(() => new object()));
                await context.Lifetime.AcquireAsync(initializationResource, token);
                context.Lifetime.CreateOwned(() => owned);
                context.Progress.GetReporter(DetailInput).Report(new Detail("initialize", 0));
            },
            Terminate = progress =>
            {
                progress.GetReporter(DetailInput).Report(new Detail("shutdown termination", 0));
                return default;
            }
        };
        await using NavigationHost host = NavigationHost.Create(ScreenCatalog.Build(screens =>
            screens.Register<Page>(RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.HideAndRetain,
                async (creation, token) =>
                {
                    construction = creation;
                    await creation.Lifetime.AcquireAsync(constructionResource, token);
                    return handler;
                })));

        NavigationOperation start = host.Start(new Page(1), new NavigationOptions
        {
            Progress = observer,
            Transition = CreateTransition(new Receiver(), TransitionEndTiming.AfterPresentation, observer)
        });
        await start.WaitAsync();
        Assert.Equal(start.Id, Assert.Single(observer.Values, value => value.Value.Name == "initialize").OperationId);
        Assert.Throws<InvalidOperationException>(() => initialization!.Lifetime.CreateOwned(() => new object()));
        int reported = observer.Values.Count;
        initialization!.Progress.GetReporter(DetailInput).Report(new Detail("late initialization", 0));
        Assert.Equal(reported, observer.Values.Count);

        await host.Client.PushAsync(host.Root, Destination.For(new Page(2)));
        await host.Client.BackAsync(host.Root);
        Assert.Equal(1, handler.Initializations);
        Assert.Equal(3, handler.Preparations);
        Assert.Equal(0, constructionResource.Releases);
        Assert.Equal(0, initializationResource.Releases);
        Assert.Equal(0, owned.AsyncDisposals);

        await host.ShutdownAsync();
        Assert.Equal(1, constructionResource.Releases);
        Assert.Equal(1, initializationResource.Releases);
        Assert.Equal(1, owned.AsyncDisposals);
        Assert.Equal(0, owned.SynchronousDisposals);
        Assert.Equal(reported, observer.Values.Count);
    }

    [Fact]
    public async Task Full_progress_does_not_complete_initialization_or_open_input_before_preparation_settlement_and_activation ()
    {
        Gate initialization = new();
        Gate preparation = new();
        Gate settlement = new();
        Gate activation = new();
        bool initializationCompleted = false;
        bool preparationCompleted = false;
        Receiver effect = new()
        {
            Settle = _ =>
            {
                Assert.True(preparationCompleted);
                return settlement.HoldAsync();
            }
        };
        Handler handler = new()
        {
            Initialize = async (context, _) =>
            {
                context.Progress.GetReporter(DetailInput).Report(new Detail("initialize", 0));
                context.Progress.GetReporter(IntegerInput).Report(1);
                context.Progress.GetReporter(FractionInput).Report(1);
                await initialization.HoldAsync();
                initializationCompleted = true;
            },
            Prepare = async (_, _, _) =>
            {
                Assert.True(initializationCompleted);
                await preparation.HoldAsync();
                preparationCompleted = true;
            },
            Activate = (_, _) =>
            {
                Assert.True(effect.Settled);
                return activation.HoldAsync();
            }
        };
        handler.View.Applying = presentation =>
        {
            if (presentation.InputEnabled)
            {
                Assert.True(handler.Active);
            }
        };
        await using NavigationHost host = CreateHost(ScreenInstancePolicy.Single, handler);
        NavigationOperation operation = host.Start(new Page(1), new NavigationOptions
        {
            Transition = CreateTransition(effect, TransitionEndTiming.AfterResourceRelease)
        });
        try
        {
            await initialization.Entered.WaitAsync(Timeout);
            Assert.Equal(operation.Id, Assert.Single(effect.Values).OperationId);
            Assert.Equal(1, Assert.Single(effect.IntegerValues).Value);
            Assert.Equal(1d, Assert.Single(effect.FractionValues).Value);
            Assert.Equal(0, handler.Preparations);
            Assert.False(effect.Settled);
            Assert.False(handler.View.Presentation.InputEnabled);
            Assert.False(operation.WaitAsync().IsCompleted);

            initialization.Open();
            await preparation.Entered.WaitAsync(Timeout);
            Assert.Equal(0, handler.Activations);
            Assert.False(effect.Settled);
            Assert.False(handler.View.Presentation.InputEnabled);

            preparation.Open();
            await settlement.Entered.WaitAsync(Timeout);
            Assert.Equal(0, handler.Activations);
            Assert.True(effect.View.Presentation.OutputEnabled);
            Assert.False(handler.View.Presentation.InputEnabled);

            settlement.Open();
            await activation.Entered.WaitAsync(Timeout);
            Assert.True(effect.Settled);
            Assert.False(handler.View.Presentation.InputEnabled);
            Assert.False(operation.WaitAsync().IsCompleted);
            activation.Open();
            await operation.WaitAsync().WaitAsync(Timeout);
            Assert.True(handler.Active);
            Assert.True(handler.View.Presentation.InputEnabled);
        }
        finally
        {
            initialization.Open();
            preparation.Open();
            settlement.Open();
            activation.Open();
        }
    }

    [Fact]
    public async Task Resource_release_timing_keeps_the_effect_subscribed_until_termination_and_release_finish_before_activation ()
    {
        Gate termination = new();
        Gate release = new();
        Gate settlement = new();
        bool terminationCompleted = false;
        bool releaseCompleted = false;
        Receiver observer = new();
        Receiver effect = new()
        {
            Settle = _ =>
            {
                Assert.True(releaseCompleted);
                return settlement.HoldAsync();
            }
        };
        Acquisition resource = new()
        {
            Release = async progress =>
            {
                Assert.True(terminationCompleted);
                progress.GetReporter(DetailInput).Report(new Detail("release", 1));
                await release.HoldAsync();
                releaseCompleted = true;
            }
        };
        Handler source = new()
        {
            Initialize = async (context, token) =>
            {
                await context.Lifetime.AcquireAsync(resource, token);
            },
            Terminate = async progress =>
            {
                progress.GetReporter(DetailInput).Report(new Detail("terminate", 1));
                await termination.HoldAsync();
                terminationCompleted = true;
            }
        };
        Handler destination = new();
        await using NavigationHost host = CreateHost(ScreenInstancePolicy.Multiple, source, destination);
        NavigationOperation start = host.Start(new Page(1), new NavigationOptions
        {
            Progress = observer,
            Transition = CreateTransition(new Receiver(), TransitionEndTiming.AfterPresentation, observer)
        });
        await start.WaitAsync();
        NavigationEntryId sourceEntry = Assert.Single(host.State.Current.Entries).Key;
        NavigationOperation operation = host.Client.Reset(host.Root, Destination.For(new Page(2)), new NavigationOptions
        {
            Progress = observer,
            Transition = CreateTransition(effect, TransitionEndTiming.AfterResourceRelease, observer)
        });
        try
        {
            await termination.Entered.WaitAsync(Timeout);
            Assert.True(source.Activity!.CancellationToken.IsCancellationRequested);
            Assert.Equal(0, resource.Releases);
            Assert.False(effect.Settled);
            Assert.True(effect.View.Presentation.OutputEnabled);
            Assert.False(destination.Active);

            termination.Open();
            await release.Entered.WaitAsync(Timeout);
            Assert.False(effect.Settled);
            Assert.True(effect.View.IsAlive);
            Assert.True(effect.View.Presentation.OutputEnabled);
            Assert.False(destination.View.Presentation.InputEnabled);
            Assert.False(operation.WaitAsync().IsCompleted);
            ProgressUpdate<Detail> stopped = Assert.Single(effect.Values, value => value.Value.Name == "terminate");
            ProgressUpdate<Detail> released = Assert.Single(effect.Values, value => value.Value.Name == "release");
            Assert.Equal(operation.Id, stopped.OperationId);
            Assert.Equal(operation.Id, released.OperationId);
            Assert.Equal(sourceEntry, stopped.EntryId);
            Assert.Equal(sourceEntry, released.EntryId);
            Assert.NotEqual(stopped.WorkId, released.WorkId);
            Assert.NotEqual(start.Id, released.OperationId);
            int reported = observer.Values.Count;
            resource.Progress!.GetReporter(DetailInput).Report(new Detail("stale acquisition", 0));
            source.TerminationProgress!.GetReporter(DetailInput).Report(new Detail("late termination", 0));
            Assert.Equal(reported, observer.Values.Count);

            release.Open();
            await settlement.Entered.WaitAsync(Timeout);
            Assert.Equal(0, destination.Activations);
            Assert.False(destination.View.Presentation.InputEnabled);
            resource.ReleaseProgress!.GetReporter(DetailInput).Report(new Detail("late release", 0));
            Assert.Equal(reported, observer.Values.Count);
            settlement.Open();
            await operation.WaitAsync().WaitAsync(Timeout);
            Assert.Equal(1, resource.Releases);
            Assert.True(destination.Active);
            Assert.True(destination.View.Presentation.InputEnabled);
            Assert.Equal(1, effect.Disposals);
        }
        finally
        {
            termination.Open();
            release.Open();
            settlement.Open();
        }
    }

    [Fact]
    public async Task Repreparation_releases_previous_resources_under_the_current_effect_before_it_settles ()
    {
        Gate release = new();
        Receiver effect = new();
        Acquisition resource = new()
        {
            Release = async progress =>
            {
                progress.GetReporter(DetailInput).Report(new Detail("release previous preparation", 1));
                await release.HoldAsync();
            }
        };
        Handler handler = new()
        {
            Prepare = async (route, preparation, token) =>
            {
                if (route.Number == 1)
                {
                    await preparation.Lifetime.AcquireAsync(resource, token);
                }
            }
        };
        await using NavigationHost host = CreateHost(ScreenInstancePolicy.Single, handler);
        await host.StartAsync(new Page(1));
        NavigationOperation operation = host.Client.Push(host.Root, Destination.For(new Page(2)), new NavigationOptions
        {
            Transition = CreateTransition(effect, TransitionEndTiming.AfterResourceRelease)
        });
        try
        {
            await release.Entered.WaitAsync(Timeout);
            Assert.False(effect.Settled);
            Assert.True(effect.View.Presentation.OutputEnabled);
            Assert.False(handler.Active);
            Assert.False(handler.View.Presentation.InputEnabled);
            Assert.Equal(operation.Id, Assert.Single(effect.Values, value => value.Value.Name == "release previous preparation").OperationId);
            release.Open();
            await operation.WaitAsync().WaitAsync(Timeout);
            Assert.Equal(1, handler.Initializations);
            Assert.Equal(1, resource.Releases);
            Assert.True(effect.Settled);
            Assert.True(handler.View.Presentation.InputEnabled);
        }
        finally
        {
            release.Open();
        }
    }

    [Fact]
    public async Task Default_timing_settles_the_effect_and_activates_the_destination_while_old_resources_release ()
    {
        Gate release = new();
        Receiver effect = new();
        Acquisition resource = new()
        {
            Release = _ =>
            {
                Assert.True(effect.Settled);
                return release.HoldAsync();
            }
        };
        Handler source = new()
        {
            Initialize = async (context, token) =>
            {
                await context.Lifetime.AcquireAsync(resource, token);
            }
        };
        Handler destination = new();
        await using NavigationHost host = CreateHost(ScreenInstancePolicy.Multiple, source, destination);
        await host.StartAsync(new Page(1));
        NavigationOperation operation = host.Client.Reset(host.Root, Destination.For(new Page(2)), new NavigationOptions
        {
            Transition = new NavigationTransition(NavigationTransitionScope.Region, (context, _) =>
            {
                context.RegisterViewAdapter(effect.View);
                return new(context.Lifetime.CreateOwned(() => effect));
            })
        });
        try
        {
            await release.Entered.WaitAsync(Timeout);
            await operation.WaitAsync().WaitAsync(Timeout);
            Assert.True(effect.Settled);
            Assert.Equal(1, effect.Disposals);
            Assert.True(destination.Active);
            Assert.True(destination.View.Presentation.InputEnabled);
            Assert.Contains(host.Terminations.Current.Records, record => record.Status == NavigationTerminationStatus.Pending);
        }
        finally
        {
            release.Open();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_canceled_initialization_keeps_the_independent_effect_until_partial_cleanup_and_source_restoration (bool cancel)
    {
        Gate acquisition = new();
        Gate termination = new();
        Gate release = new();
        Gate settlement = new();
        Receiver effect = new()
        {
            Settle = _ => settlement.HoldAsync()
        };
        Acquisition resource = new()
        {
            Fail = !cancel,
            Acquire = (_, token) => acquisition.HoldAsync(token),
            Release = async progress =>
            {
                progress.GetReporter(DetailInput).Report(new Detail("partial release", 0));
                await release.HoldAsync();
            }
        };
        Handler source = new();
        Handler destination = new()
        {
            Initialize = async (context, token) =>
            {
                await context.Lifetime.AcquireAsync(resource, token);
            },
            Terminate = async progress =>
            {
                progress.GetReporter(DetailInput).Report(new Detail("terminate canceled initialization", 0));
                await termination.HoldAsync();
            }
        };
        await using NavigationHost host = CreateHost(ScreenInstancePolicy.Multiple, source, destination);
        await host.StartAsync(new Page(1));
        NavigationOperation operation = host.Client.Push(host.Root, Destination.For(new Page(2)), new NavigationOptions
        {
            Transition = CreateTransition(effect, TransitionEndTiming.AfterResourceRelease)
        });
        try
        {
            await acquisition.Entered.WaitAsync(Timeout);
            if (cancel)
            {
                Assert.True(operation.TryRequestCancellation());
            }
            acquisition.Open();
            await termination.Entered.WaitAsync(Timeout);
            Assert.False(effect.Settled);
            Assert.True(effect.View.Presentation.OutputEnabled);
            Assert.Equal(0, resource.Releases);
            termination.Open();

            await release.Entered.WaitAsync(Timeout);
            Assert.False(effect.Settled);
            Assert.Equal(0, effect.Disposals);
            Assert.True(effect.View.IsAlive);
            Assert.True(effect.View.Presentation.OutputEnabled);
            Assert.False(operation.WaitAsync().IsCompleted);
            Assert.False(source.View.Presentation.InputEnabled);
            Assert.Equal(operation.Id, Assert.Single(effect.Values, value => value.Value.Name == "partial release").OperationId);
            Assert.Equal(operation.Id, Assert.Single(effect.Values, value => value.Value.Name == "terminate canceled initialization").OperationId);
            release.Open();

            await settlement.Entered.WaitAsync(Timeout);
            Assert.Equal(1, resource.Releases);
            Assert.False(source.Active);
            Assert.Equal(TransitionSettlementTarget.Source, effect.SettlementTarget);
            settlement.Open();
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync().WaitAsync(Timeout));
            }
            else
            {
                NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => operation.WaitAsync().WaitAsync(Timeout));
                Assert.False(failure.DestinationCommitted);
            }
            Assert.Equal(0, destination.Preparations);
            Assert.Equal(0, destination.Activations);
            Assert.Equal(new Page(1), Assert.Single(host.State.Current.Entries).Value.Route);
            Assert.True(source.Active);
            Assert.True(source.View.Presentation.InputEnabled);
            Assert.Equal(1, effect.Disposals);
        }
        finally
        {
            acquisition.Open();
            termination.Open();
            release.Open();
            settlement.Open();
        }
    }

    [Fact]
    public async Task Failed_release_is_reported_and_is_not_retried_by_shutdown ()
    {
        Acquisition resource = new()
        {
            Release = _ => throw new InvalidOperationException("Release failed.")
        };
        Handler source = new()
        {
            Initialize = async (context, token) =>
            {
                await context.Lifetime.AcquireAsync(resource, token);
            }
        };
        Handler destination = new();
        Receiver effect = new();
        NavigationHost host = CreateHost(ScreenInstancePolicy.Multiple, source, destination);
        await host.StartAsync(new Page(1));
        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => host.Client.Reset(host.Root, Destination.For(new Page(2)), new NavigationOptions
        {
            Transition = CreateTransition(effect, TransitionEndTiming.AfterResourceRelease)
        }).WaitAsync().WaitAsync(Timeout));
        Assert.True(failure.DestinationCommitted);
        Assert.Equal(NavigationPresentationStatus.Ready, failure.PresentationStatus);
        Assert.Equal(1, resource.Releases);
        Assert.Contains(host.Terminations.Current.Records, record => record.Status == NavigationTerminationStatus.Failed);
        Assert.True(effect.Settled);
        Assert.Equal(TransitionSettlementTarget.Destination, effect.SettlementTarget);
        Assert.True(destination.Active);
        Assert.True(destination.View.Presentation.InputEnabled);

        await Assert.ThrowsAsync<AggregateException>(() => host.ShutdownAsync().AsTask().WaitAsync(Timeout));
        await Assert.ThrowsAsync<AggregateException>(() => host.ShutdownAsync().AsTask().WaitAsync(Timeout));
        Assert.Equal(1, resource.Releases);
    }

    [Fact]
    public async Task Receiver_failures_during_termination_and_release_become_diagnostics_without_skipping_cleanup ()
    {
        Acquisition resource = new()
        {
            Release = progress =>
            {
                progress.GetReporter(DetailInput).Report(new Detail("release", 0));
                return default;
            }
        };
        Handler source = new()
        {
            Initialize = async (context, token) =>
            {
                await context.Lifetime.AcquireAsync(resource, token);
            },
            Terminate = progress =>
            {
                progress.GetReporter(DetailInput).Report(new Detail("terminate", 0));
                return default;
            }
        };
        Handler destination = new();
        Receiver effect = new();
        await using NavigationHost host = CreateHost(ScreenInstancePolicy.Multiple, source, destination);
        await host.StartAsync(new Page(1));
        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => host.Client.Reset(host.Root, Destination.For(new Page(2)), new NavigationOptions
        {
            Transition = CreateTransition(effect, TransitionEndTiming.AfterResourceRelease, new Receiver
            {
                ThrowOnSample = true
            })
        }).WaitAsync().WaitAsync(Timeout));

        Assert.True(failure.DestinationCommitted);
        Assert.Contains(failure.Diagnostics, diagnostic => diagnostic.Reason.Contains("Observer failed.", StringComparison.Ordinal));
        Assert.Single(effect.Values, value => value.Value.Name == "terminate");
        Assert.Single(effect.Values, value => value.Value.Name == "release");
        Assert.Equal(1, resource.Releases);
        Assert.True(effect.Settled);
        Assert.True(destination.Active);
        Assert.Empty(host.Terminations.Current.Records);
    }

    [Fact]
    public async Task Source_owned_effect_cannot_cover_its_own_release_and_is_rejected_before_deactivation ()
    {
        Receiver effect = new();
        Acquisition resource = new();
        Handler source = new()
        {
            TransitionEffect = effect,
            Initialize = async (context, token) =>
            {
                await context.Lifetime.AcquireAsync(resource, token);
            }
        };
        Handler destination = new();
        await using NavigationHost host = CreateHost(ScreenInstancePolicy.Multiple, source, destination);
        await host.StartAsync(new Page(1));
        NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => host.Client.Reset(host.Root, Destination.For(new Page(2)), new NavigationOptions
        {
            Transition = NavigationTransition.FromSourceScreen(NavigationTransitionScope.Region, TransitionEndTiming.AfterResourceRelease)
        }).WaitAsync().WaitAsync(Timeout));

        Assert.IsType<NavigationConfigurationException>(failure.InnerException);
        Assert.False(failure.DestinationCommitted);
        Assert.True(source.Active);
        Assert.True(source.View.Presentation.InputEnabled);
        Assert.Equal(0, source.Deactivations);
        Assert.Equal(0, source.Terminations);
        Assert.Equal(0, destination.Initializations);
        Assert.Equal(0, effect.Begins);
        Assert.Equal(0, effect.Disposals);
        Assert.Equal(0, resource.Releases);
        Assert.Equal(new Page(1), Assert.Single(host.State.Current.Entries).Value.Route);
    }

    [Fact]
    public async Task Failed_destination_owned_effect_settles_before_its_own_screen_resources_release ()
    {
        Gate release = new();
        Acquisition resource = new()
        {
            Release = _ => release.HoldAsync()
        };
        Receiver effect = new()
        {
            PrepareSwitch = _ => throw new InvalidOperationException("Transition preparation failed."),
            Settle = _ =>
            {
                Assert.Equal(0, resource.Releases);
                return default;
            }
        };
        Handler destination = new()
        {
            TransitionEffect = effect,
            Initialize = async (context, token) =>
            {
                await context.Lifetime.AcquireAsync(resource, token);
            },
            Terminate = _ =>
            {
                Assert.True(effect.Settled);
                return default;
            }
        };
        await using NavigationHost host = CreateHost(ScreenInstancePolicy.Single, destination);
        NavigationOperation operation = host.Start(new Page(1), new NavigationOptions
        {
            Transition = NavigationTransition.FromDestinationScreen(NavigationTransitionScope.Region, TransitionEndTiming.AfterResourceRelease)
        });
        try
        {
            await release.Entered.WaitAsync(Timeout);
            Assert.Equal(1, effect.Begins);
            Assert.True(effect.Settled);
            Assert.False(effect.View.Presentation.OutputEnabled);
            Assert.False(destination.Active);
            Assert.False(operation.WaitAsync().IsCompleted);
            release.Open();
            NavigationException failure = await Assert.ThrowsAsync<NavigationException>(() => operation.WaitAsync().WaitAsync(Timeout));
            Assert.False(failure.DestinationCommitted);
            Assert.Empty(host.State.Current.Entries);
            Assert.Equal(1, resource.Releases);
            Assert.Equal(1, effect.Disposals);
        }
        finally
        {
            release.Open();
        }
    }

    private static NavigationHost CreateHost (ScreenInstancePolicy instancePolicy, params Handler[] handlers)
    {
        Queue<Handler> pending = new(handlers);
        return NavigationHost.Create(ScreenCatalog.Build(screens => screens.Register(
            RouteEntryOperations.Reset | RouteEntryOperations.Push, LowerPresentationPolicy.HideAndRetain,
            new ScreenDefinition<Page>((creation, _) =>
            {
                Handler handler = pending.Dequeue();
                View view = creation.Lifetime.CreateOwned(() => handler.View);
                ScreenPresentationExtensions.ConnectPresentation(creation, new ScreenPresentationBinding(new[] { view }));
                if (handler.TransitionEffect is Receiver effect)
                {
                    creation.SetTransitionEffect(creation.Lifetime.CreateOwned(() => effect), effect.View, Reports, effect.Observe);
                }
                return new(handler);
            }, instancePolicy))));
    }

    private static NavigationTransition CreateTransition (Receiver effect, TransitionEndTiming timing, Receiver? observer = null) => NavigationTransition.Create(NavigationTransitionScope.Region, Reports, (context, source, _) =>
    {
        context.RegisterViewAdapter(effect.View);
        context.ObserveProgress(source, effect.Observe);
        if (observer is not null)
        {
            context.ObserveProgress(source, observer.Observe);
        }
        return new(context.Lifetime.CreateOwned(() => effect));
    }, endTiming: timing);

    private sealed record ReportState (ProgressUpdate<Detail>? Detail = null, ProgressUpdate<int>? Integer = null, ProgressUpdate<double?>? Fraction = null);
    private sealed record Page (int Number) : Route;
    private readonly record struct Detail (string Name, int Number);

    private sealed class Handler : IScreenLifecycleHandler<Page>
    {
        public Func<ScreenInitializationContext, CancellationToken, ValueTask>? Initialize { get; init; }
        public Func<Page, ScreenPreparationContext, CancellationToken, ValueTask>? Prepare { get; init; }
        public Func<Page, ScreenActivityContext, ValueTask>? Activate { get; init; }
        public Func<NavigationProgressReporter, ValueTask>? Terminate { get; init; }
        public View View { get; } = new();
        public Receiver? TransitionEffect { get; init; }
        public ScreenActivityContext? Activity { get; private set; }
        public NavigationProgressReporter? Progress { get; private set; }
        public NavigationProgressReporter? TerminationProgress { get; private set; }
        public bool Active { get; private set; }
        public int Initializations { get; private set; }
        public int Preparations { get; private set; }
        public int Activations { get; private set; }
        public int Deactivations { get; private set; }
        public int Terminations { get; private set; }
        public async ValueTask InitializeAsync (ScreenInitializationContext initialization, CancellationToken cancellationToken)
        {
            Initializations++;
            if (Initialize is not null)
            {
                await Initialize(initialization, cancellationToken);
            }
        }
        public async ValueTask PrepareAsync (Page route, ScreenPreparationContext preparation, CancellationToken cancellationToken)
        {
            Preparations++;
            Progress = preparation.Progress;
            preparation.Progress.GetReporter(DetailInput).Report(new Detail("prepare", route.Number));
            if (Prepare is not null)
            {
                await Prepare(route, preparation, cancellationToken);
            }
        }
        public async ValueTask ActivateAsync (Page route, ScreenActivityContext activity)
        {
            Activations++;
            Activity = activity;
            if (Activate is not null)
            {
                await Activate(route, activity);
            }
            Active = true;
        }
        public ValueTask DeactivateAsync ()
        {
            Deactivations++;
            Active = false;
            return default;
        }
        public async ValueTask TerminateAsync (NavigationProgressReporter progress)
        {
            Terminations++;
            TerminationProgress = progress;
            if (Terminate is not null)
            {
                await Terminate(progress);
            }
        }
    }

    private sealed class Acquisition : IResourceAcquisition<string>
    {
        public NavigationProgressReporter? Progress { get; private set; }
        public NavigationProgressReporter? ReleaseProgress { get; private set; }
        public Func<ResourceAcquisitionContext, CancellationToken, ValueTask>? Acquire { get; init; }
        public Func<NavigationProgressReporter, ValueTask>? Release { get; init; }
        public bool Fail { get; init; }
        public int Releases { get; private set; }
        public async ValueTask<string> AcquireAsync (ResourceAcquisitionContext context, CancellationToken cancellationToken)
        {
            Progress = context.Progress;
            context.Progress.GetReporter(DetailInput).Report(new Detail("acquire", 0));
            if (Acquire is not null)
            {
                await Acquire(context, cancellationToken);
            }
            if (Fail)
            {
                throw new InvalidOperationException("Acquisition failed.");
            }
            return "loaded";
        }
        public async ValueTask ReleaseAsync (NavigationProgressReporter progress)
        {
            Releases++;
            ReleaseProgress = progress;
            if (Release is not null)
            {
                await Release(progress);
            }
        }
    }

    private sealed class Receiver : IProgress<NavigationProgress>, INavigationTransitionEffect, IAsyncDisposable
    {
        public List<ProgressUpdate<Detail>> Values { get; } = new();
        public List<ProgressUpdate<int>> IntegerValues { get; } = new();
        public List<ProgressUpdate<double?>> FractionValues { get; } = new();
        public View View { get; } = new();
        public Func<TransitionTargetsContext, ValueTask>? PrepareSwitch { get; init; }
        public Func<TransitionSettlementContext, ValueTask>? Settle { get; init; }
        public TransitionSettlementTarget? SettlementTarget { get; private set; }
        public bool ThrowOnSample { get; init; }
        public bool Settled { get; private set; }
        public int Begins { get; private set; }
        public int Disposals { get; private set; }
        public void Report (NavigationProgress progress)
        {
        }
        public void Observe (ReportState state)
        {
            if (state.Detail is { } detail && (Values.Count == 0 || !Values[Values.Count - 1].Equals(detail)))
            {
                Report(detail);
            }
            if (state.Integer is { } integer && (IntegerValues.Count == 0 || !IntegerValues[IntegerValues.Count - 1].Equals(integer)))
            {
                IntegerValues.Add(integer);
            }
            if (state.Fraction is { } fraction && (FractionValues.Count == 0 || !FractionValues[FractionValues.Count - 1].Equals(fraction)))
            {
                FractionValues.Add(fraction);
            }
        }
        private void Report (ProgressUpdate<Detail> progress)
        {
            if (ThrowOnSample)
            {
                throw new InvalidOperationException("Observer failed.");
            }
            Values.Add(progress);
        }
        public ValueTask BeginAsync (TransitionBeginContext context, CancellationToken cancellationToken)
        {
            Begins++;
            return default;
        }
        public ValueTask PrepareSwitchAsync (TransitionTargetsContext context, CancellationToken cancellationToken) => PrepareSwitch?.Invoke(context) ?? default;
        public ValueTask AfterCommitAsync (TransitionTargetsContext context, CancellationToken cancellationToken) => default;
        public async ValueTask SettleAsync (TransitionSettlementContext context, CancellationToken cancellationToken)
        {
            SettlementTarget = context.Target;
            if (Settle is not null)
            {
                await Settle(context);
            }
            Settled = true;
        }
        public ValueTask DisposeAsync ()
        {
            Disposals++;
            View.Dispose();
            return default;
        }
    }

    private sealed class Gate
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => entered.Task;
        public async ValueTask HoldAsync (CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            await proceed.Task.WaitAsync(cancellationToken);
        }
        public void Open () => proceed.TrySetResult();
    }

    private sealed class OwnedResource : IDisposable, IAsyncDisposable
    {
        public int SynchronousDisposals { get; private set; }
        public int AsyncDisposals { get; private set; }
        public void Dispose () => SynchronousDisposals++;
        public ValueTask DisposeAsync ()
        {
            AsyncDisposals++;
            return default;
        }
    }

    private sealed class View : IViewAdapter, IDisposable
    {
        public object Identity => this;
        public object OrderingDomain => typeof(View);
        public bool IsAlive { get; private set; } = true;
        public ViewPresentation Presentation { get; private set; } = new(false, false, 0);
        public Action<ViewPresentation>? Applying { get; set; }
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
        public void Apply (ViewPresentation presentation)
        {
            Applying?.Invoke(presentation);
            Presentation = presentation;
        }
        public void Dispose () => IsAlive = false;
    }
}
