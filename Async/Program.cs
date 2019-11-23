using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Async
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            async Task<T> TaskTemplate<T>(int milliseconds, T defaultValue)
            {
                Debug.WriteLine($"Started TaskTemplate({defaultValue})");
                await Task.Delay((int)(milliseconds * 2));
                Debug.WriteLine($"Finished TaskTemplate({defaultValue})");
                return defaultValue;
            }

            async Task<(T1, T2)> Local<T1, T2>(int timeout, T1 value1, T2 value2)
            {
                var timespan = TimeSpan.FromSeconds(timeout);
                var milliseconds = (int)timespan.TotalMilliseconds;
                using var source = new CancellationTokenSource();

                var token = source.Token;

                (Task<T1> T1, Task<T2> T2) tasks = (
                    TaskTemplate<T1>(milliseconds, value1),
                    TaskTemplate<T2>(milliseconds, value2));

                token.Register(t =>
                {
                    var (tkn, tsk) = ((CancellationToken tkn, (Task<T1> T1, Task<T2> T2) tsk))t;

                    Debug.WriteLine($"T1 = {tsk.T1.Id} {tsk.T1.Status}");
                    Debug.WriteLine($"T2 = {tsk.T2.Id} {tsk.T2.Status}");

                    tsk.T1.Terminate(token);
                    tsk.T2.Terminate(token);

                    Debug.WriteLine($"tkn.IsCancellationRequested = {tkn.IsCancellationRequested}");
                    Debug.WriteLine($"T1 = {tsk.T1.Id} {tsk.T1.Status}");
                    Debug.WriteLine($"T2 = {tsk.T2.Id} {tsk.T2.Status}");
                }, (token, tasks));

                try
                {
                    source.CancelAfter(milliseconds / 2);

                    return await await tasks.AsTask(token);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                    return (tasks.T1.IsCompletedSuccessfully ? tasks.T1.Result : default
                        , tasks.T2.IsCompletedSuccessfully ? tasks.T2.Result : default);
                }
            }

            var result = await Local<int, decimal>(10, value1: 10, value2: 20m);
            Debug.WriteLine($"{result.Item1:g}, {result.Item2:g}");
        }
    }

    public static class Extensions
    {
        public static async Task<Task<(T1, T2)>> AsTask<T1, T2>(this (Task<T1> T1, Task<T2> T2) pair, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<(T1,T2)>();

            try
            {
                var tasks = new Task[] {
                    await pair.T1.TaskRunner<T1>(token)
                    , await pair.T2.TaskRunner<T2>(token)
                };

                await Task.WhenAll(tasks);

                Debug.WriteLine("All completed.");
                tcs.SetResult((pair.T1.Result, pair.T2.Result));
            }
            catch (TaskCanceledException)
            {
                tcs.SetCanceled();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            return tcs.Task;
        }

        public static void Terminate(this Task task, CancellationToken token)
        {
            var finish = typeof(Task).GetMethods(
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).First(mi => mi.Name == "Finish");

            var assignCancellationToken = typeof(Task).GetMethods(
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).First(mi => mi.Name == "AssignCancellationToken");

            var setCancellationAcknowledged = typeof(Task).GetMethods(
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).First(mi => mi.Name == "SetCancellationAcknowledged");

            assignCancellationToken.Invoke(task, new object[] { token, null, null });
            setCancellationAcknowledged.Invoke(task, new object[0]);
            finish.Invoke(task, new object[] { false });
        }

        private static Task<Task<T1>> TaskRunner<T1>(this Task<T1> func, CancellationToken token)
        {
            var task = new TaskFactory(token)
                .StartNew<Task<T1>>(
                    async _ => await func
                    , token, TaskCreationOptions.AttachedToParent);

            return task;
        }

    }
}
