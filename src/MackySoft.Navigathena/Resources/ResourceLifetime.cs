using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    /// <summary>Lets an external owner stop all registered users before destroying its resources.</summary>
    public sealed class ResourceLifetime
    {
        private readonly object sync = new();
        private readonly HashSet<IResourceUser> users = new();
        private Task? ending;

        public ResourceReference<T> Reference<T> (T resource) where T : class
        {
            if (resource is null)
            {
                throw new ArgumentNullException(nameof(resource));
            }

            lock (sync)
            {
                ThrowIfEnding();
                return new ResourceReference<T>(this, resource);
            }
        }

        /// <summary>Closes new borrowing and waits for users to stop. Does not destroy externally owned values. Concurrent calls join the same attempt, including a failed attempt.</summary>
        public ValueTask EndAsync ()
        {
            TaskCompletionSource<object?>? completion = null;
            IResourceUser[] snapshot = Array.Empty<IResourceUser>();
            Task task;
            lock (sync)
            {
                if (ending is null)
                {
                    completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    ending = completion.Task;
                    snapshot = users.ToArray();
                }

                task = ending;
            }

            if (completion is not null)
            {
                _ = EndUsersAsync(snapshot, completion);
            }

            return new ValueTask(task);
        }

        internal void AddUser (IResourceUser user)
        {
            lock (sync)
            {
                ThrowIfEnding();
                users.Add(user);
            }
        }

        internal void RemoveUser (IResourceUser user)
        {
            lock (sync)
            {
                users.Remove(user);
            }
        }

        private void ThrowIfEnding ()
        {
            if (ending is not null)
            {
                throw new InvalidOperationException("The resource lifetime is ending; new references and users are closed.");
            }
        }

        private static async Task EndUsersAsync (IReadOnlyList<IResourceUser> snapshot, TaskCompletionSource<object?> completion)
        {
            try
            {
                await Task.WhenAll(snapshot.Select(static user => user.RequestEndAsync().AsTask()));
                completion.TrySetResult(null);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }
    }
}
