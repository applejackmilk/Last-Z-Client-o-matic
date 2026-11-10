using NLog;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace WindowsMCP_Integrated
{
    public sealed class MethodContext
    {
        public required string MethodName { get; init; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset EndedAt { get; set; }
        public Exception? Exception { get; set; }
    }
    public interface IMethodHook
    {
        int Priority { get; }
        Task BeforeAsync(MethodContext ctx);
        Task AfterAsync(MethodContext ctx);
    }

    public class HookedProxy<T> : DispatchProxy where T : class
    {
        private T _target = default!;
        private ILogger _logger = default!;
        private IReadOnlyList<IMethodHook> _hooks = [];

        public static T Create(T target, ILogger logger,
                               IEnumerable<IMethodHook> hooks)
        {
            var proxy = Create<T, HookedProxy<T>>();
            var self = (HookedProxy<T>)(object)proxy;
            self._target = target;
            self._logger = logger;
            self._hooks = hooks.ToList();
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var ctx = new MethodContext { MethodName = method!.Name };
            var rt = method.ReturnType;

            if (rt == typeof(Task))
                return RunAsync<object?>(ctx, async () =>
                {
                    await (Task)Call(method, args)!;
                    return null;
                });

            if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
                return typeof(HookedProxy<T>)
                    .GetMethod(nameof(RunTaskOfT), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(rt.GetGenericArguments()[0])
                    .Invoke(this, [method, args, ctx]);

            // Synchronous method: hooks are blocked on (see caveats)
            return RunAsync<object?>(ctx, () => Task.FromResult(Call(method, args)))
                   .GetAwaiter().GetResult();
        }

        private Task<TResult> RunTaskOfT<TResult>(MethodInfo m, object?[]? args, MethodContext ctx)
            => RunAsync(ctx, () => (Task<TResult>)Call(m, args)!);

        private async Task<TResult> RunAsync<TResult>(MethodContext ctx, Func<Task<TResult>> call)
        {
            await RunHooksAsync(h => h.BeforeAsync(ctx), isAfter: false);

            ctx.StartedAt = DateTimeOffset.UtcNow;
            _logger.Info("{Method} started at {Time:O}", ctx.MethodName, ctx.StartedAt);
            try
            {
                var result = await call();
                return result;
            }
            catch (Exception ex)
            {
                ctx.Exception = ex;
                _logger.Error(ex, "{Method} failed", ctx.MethodName);
                throw;
            }
            finally
            {
                ctx.EndedAt = DateTimeOffset.UtcNow;
                _logger.Info("{Method} ended at {Time:O} ({Ms:F1} ms)",
                    ctx.MethodName, ctx.EndedAt, (ctx.EndedAt - ctx.StartedAt).TotalMilliseconds);

                await RunHooksAsync(h => h.AfterAsync(ctx), isAfter: true);
            }
        }

        private Task RunHooksAsync(Func<IMethodHook, Task> run, bool isAfter)
        {
            var ordered = _hooks.OrderBy(h => h.Priority);

            return RunSequentially(ordered, run);
        }

        private static async Task RunSequentially(IEnumerable<IMethodHook> hooks, Func<IMethodHook, Task> run)
        {
            foreach (var h in hooks) await run(h);
        }

        private object? Call(MethodInfo m, object?[]? args)
        {
            try { return m.Invoke(_target, args); }
            catch (TargetInvocationException ex)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                throw; // unreachable
            }
        }
    }
}
