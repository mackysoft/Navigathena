using System;
using System.Threading;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    internal static class ScreenWorkExecution
    {
        private static readonly AsyncLocal<Execution?> Current = new();
        public static NavigationEntryId? EntryId => Active?.Entry;
        public static Guid? WorkId => Active?.Id;
        public static CancellationToken CancellationToken => Active?.Token ?? default;
        private static Execution? Active => Current.Value is { active: true } execution ? execution : null;

        public static async ValueTask RunAsync (Guid id, NavigationEntryId entry, CancellationToken token, Func<ValueTask> callback)
        {
            Execution? previous = Current.Value;
            Execution execution = new(id, entry, token);
            Current.Value = execution;
            try
            {
                await callback();
            }
            finally
            {
                execution.active = false;
                Current.Value = previous;
            }
        }

        private sealed record Execution (Guid Id, NavigationEntryId Entry, CancellationToken Token)
        {
            internal volatile bool active = true;
        }
    }
}
