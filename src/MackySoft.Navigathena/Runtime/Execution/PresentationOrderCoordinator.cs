using System;
using System.Collections.Generic;
using System.Linq;
using MackySoft.Navigathena.Presentation;
using MackySoft.Navigathena.Runtime.Blockers;
using MackySoft.Navigathena.Runtime.Composition;
using MackySoft.Navigathena.Runtime.Screens;
using MackySoft.Navigathena.Runtime.Views;

namespace MackySoft.Navigathena.Runtime.Execution
{
    /// <summary>Allocates one ordered view sequence, including outgoing screens, blockers and active transition effects.</summary>
    internal sealed class PresentationOrderCoordinator
    {
        private readonly object sync = new();
        private readonly ScreenRuntime runtime;
        private readonly BlockerCoordinator blockers;
        private readonly List<(object Effect, IReadOnlyList<ViewRegistration> Views)> effects = new();
        private List<ScreenInstance> order = new();

        public PresentationOrderCoordinator (ScreenRuntime runtime, BlockerCoordinator blockers)
        {
            this.runtime = runtime;
            this.blockers = blockers;
        }

        public void Validate (NavigationState state, EffectiveComposition composition)
        {
            lock (sync)
            {
                CreatePlan(state, composition).Validate();
            }
        }

        public void Apply (NavigationState state, EffectiveComposition composition)
        {
            lock (sync)
            {
                Plan plan = CreatePlan(state, composition);
                plan.Validate();
                order = plan.Screens;
                plan.Apply();
            }
        }

        public void Refresh ()
        {
            lock (sync)
            {
                NavigationState state = runtime.State.Current;
                Apply(state, CompositionDeriver.Derive(runtime.Catalog.Definition, state));
            }
        }

        public void AddEffect (object effect, IReadOnlyList<ViewRegistration> views)
        {
            lock (sync)
            {
                effects.Add((effect, views));
                Refresh();
            }
        }

        public void RemoveEffect (object effect)
        {
            lock (sync)
            {
                if (effects.RemoveAll(item => ReferenceEquals(item.Effect, effect)) > 0)
                {
                    Refresh();
                }
            }
        }

        private Plan CreatePlan (NavigationState state, EffectiveComposition composition)
        {
            var placements = blockers.GetOrderPlacements(state, composition);
            List<ScreenInstance> screens = composition.Presentations.Select(item => runtime.Find(state.GetPresentation(item.EntryId)))
                .Where(screen => screen is not null && !screen.IsEnding).Cast<ScreenInstance>().Distinct().ToList();
            int insertion = 0;
            foreach (ScreenInstance previous in order)
            {
                int index = screens.IndexOf(previous);
                if (index >= 0)
                {
                    insertion = index + 1;
                }
                else if ((!previous.IsTerminated && previous.Presentation.OutputEnabled) || placements.Any(item => ReferenceEquals(item.Screen, previous)))
                {
                    screens.Insert(insertion++, previous);
                }
            }
            foreach (var placement in placements.Where(item => !screens.Contains(item.Screen)))
            {
                screens.Add(placement.Screen);
            }
            List<ViewRegistration> views = new();
            foreach (ScreenInstance screen in screens)
            {
                foreach (var placement in placements.Where(item => ReferenceEquals(item.Screen, screen)))
                {
                    AddGroup(views, placement.Blocker.Preparation.Registrations);
                }
                AddGroup(views, screen.Creation.Registrations);
            }
            foreach (var effect in effects)
            {
                AddGroup(views, effect.Views);
            }
            return new Plan(screens, views);
        }

        private static void AddGroup (List<ViewRegistration> target, IEnumerable<ViewRegistration> views)
        {
            // Inspector/native order is the group's relative order. Registration order breaks equal-order ties.
            target.AddRange(views.Where(view => !view.IsReleased && view.Adapter.IsAlive).OrderBy(view => view.Original.Order));
        }

        private sealed class Plan
        {
            private readonly IReadOnlyList<ViewRegistration> views;
            public Plan (List<ScreenInstance> screens, IReadOnlyList<ViewRegistration> views)
            {
                Screens = screens;
                this.views = views;
            }
            public List<ScreenInstance> Screens
            {
                get;
            }
            public void Validate ()
            {
                for (int i = 0; i < views.Count; i++)
                {
                    views[i].ValidateOrder(i);
                }
            }
            public void Apply ()
            {
                List<Exception> failures = new();
                for (int i = 0; i < views.Count; i++)
                {
                    try
                    {
                        views[i].SetOrder(i);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
                if (failures.Count > 0)
                {
                    throw new AggregateException("Presentation order could not be applied to all views.", failures);
                }
            }
        }
    }
}
