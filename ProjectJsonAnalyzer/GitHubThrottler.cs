using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectJsonAnalyzer
{
    class GitHubThrottler
    {
        object _lockObject = new object();
        int _executingOperations = 0;
        ManualResetEvent _requestCompleted = new ManualResetEvent(false);

        int _remainingRequests = 10;
        DateTimeOffset _resetTime = DateTimeOffset.UtcNow;
        

        class GitHubTask
        {
            public Func<Task<IRateLimit>> Action { get; }
            public TaskCompletionSource<IRateLimit> CompletionSource { get; }

            public GitHubTask(Func<Task<IRateLimit>> action)
            {
                Action = action;
                CompletionSource = new TaskCompletionSource<IRateLimit>();
            }
        }

        public GitHubThrottler()
        {
        }

        public async Task<T> RunAsync<T>(Func<Task<T>> action) where T : IRateLimit
        {
            var result = await RunAsync2(async () => (IRateLimit) await action());
            return (T)result;
        }

        Task<IRateLimit> RunAsync2(Func<Task<IRateLimit>> action)
        {
            var githubTask = new GitHubTask(action);
            RunGitHubTaskThrottled(githubTask);
            return githubTask.CompletionSource.Task;
        }

        async void RunGitHubTaskThrottled(GitHubTask task)
        {
            try
            {
                while (true)
                {
                    bool canRunNow = false;
                    DateTimeOffset resetTime = DateTimeOffset.MinValue;
                    while (!canRunNow)
                    {
                        lock (_lockObject)
                        {
                            if (_executingOperations < _remainingRequests)
                            {
                                _executingOperations++;
                                canRunNow = true;
                                resetTime = _resetTime;
                            }
                        }

                        if (!canRunNow)
                        {
                            //  Wait until the reset time is reached or until another request has completed
                            //  (which will update the remaining requests and reset time, possibly allowing
                            //  this request to be executed)
                            Task delayTask = Task.Delay(resetTime.Subtract(DateTimeOffset.UtcNow));
                            Task requestCompletedTask = _requestCompleted.WaitOneAsync();

                            if (await Task.WhenAny(delayTask, requestCompletedTask) == requestCompletedTask)
                            {
                                //  Reset "request completed" WaitHandle
                                //  If multiple requests are waiting, then they should all get signalled and
                                //  then they'll all Reset the WaitHandle.  It only needs to be reset once but
                                //  doing it multiple times shouldn't hurt (I think)
                                _requestCompleted.Reset();
                            }

                        }
                    }

                    if (await RunGitHubTask(task))
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                task.CompletionSource.SetException(ex);
            }
        }

        //  Returns true if the request succeeded or failed, false if it hit the rate limit
        //  (and thus should be retried later)
        async Task<bool> RunGitHubTask(GitHubTask task)
        {
            try
            {
                IRateLimit result = await task.Action();

                lock (_lockObject)
                {
                    _remainingRequests = result.RateLimit.Remaining;
                    _resetTime = result.RateLimit.Reset;
                }

                task.CompletionSource.SetResult(result);
                _requestCompleted.Set();
            }
            catch (RateLimitExceededException ex)
            {
                lock (_lockObject)
                {
                    _remainingRequests = ex.Remaining;
                    _resetTime = ex.Reset;
                }

                return false;
            }
            catch (Exception ex)
            {
                task.CompletionSource.SetException(ex);
            }
            finally
            {
                lock (_lockObject)
                {
                    _executingOperations--;
                }
            }
            return true;
        }
    }

    static class WaitHandleExtensions
    {
        public static Task WaitOneAsync(this WaitHandle waitHandle)
        {
            if (waitHandle == null)
                throw new ArgumentNullException("waitHandle");

            var tcs = new TaskCompletionSource<bool>();
            var rwh = ThreadPool.RegisterWaitForSingleObject(waitHandle,
                delegate { tcs.TrySetResult(true); }, null, -1, true);
            var t = tcs.Task;
            t.ContinueWith((antecedent) => rwh.Unregister(null));
            return t;
        }
    }
}
