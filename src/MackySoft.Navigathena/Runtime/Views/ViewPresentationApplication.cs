using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MackySoft.Navigathena.Runtime.Views
{
    /// <summary>Groups native changes without deciding which views are allowed input.</summary>
    internal static class ViewPresentationApplication
    {
        public static async ValueTask ApplyAsync (ViewPresentationChangeSet changes)
        {
            List<Exception> failures = new();
            foreach (var group in changes.Changes.GroupBy(change => (change.View as IViewPresentationBatchAdapter)?.PresentationBatch))
            {
                try
                {
                    ViewPresentationChange[] values = group.ToArray();
                    if (group.Key is IViewPresentationBatch batch)
                    {
                        await batch.ApplyAsync(new ViewPresentationChangeSet(values));
                    }
                    else
                    {
                        foreach (ViewPresentationChange change in values)
                        {
                            try
                            {
                                change.View.Validate(change.Presentation);
                                change.View.Apply(change.Presentation);
                            }
                            catch (Exception exception)
                            {
                                failures.Add(exception);
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (failures.Count > 0)
            {
                throw new AggregateException("The presentation change set could not be applied safely.", failures);
            }
        }
    }
}
