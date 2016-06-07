using Octokit;
using Serilog;
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
        ILogger _logger;

        object _lockObject = new object();
        int _executingOperations = 0;
        ManualResetEvent _requestCompleted = new ManualResetEvent(false);

        int _remainingRequests = 10;
        int _limit = 10;
        DateTimeOffset _resetTime = DateTimeOffset.UtcNow;
        

        class GitHubTask
        {
            public Func<Task<IRateLimit>> Action { get; }
            public object OperationDescription { get; }
            public TaskCompletionSource<IRateLimit> CompletionSource { get; }

            public GitHubTask(Func<Task<IRateLimit>> action, object operationDescription)
            {
                Action = action;
                OperationDescription = operationDescription;
                CompletionSource = new TaskCompletionSource<IRateLimit>();
            }
        }

        public GitHubThrottler(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<T> RunAsync<T>(Func<Task<T>> action, object operationDescription) where T : IRateLimit
        {
            var result = await RunAsync2(async () => (IRateLimit) await action(), operationDescription);
            return (T)result;
        }

        Task<IRateLimit> RunAsync2(Func<Task<IRateLimit>> action, object operationDescription)
        {
            var githubTask = new GitHubTask(action, operationDescription);
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
                            _logger.Information("{@Operation} waiting for rate limit reset or finished request", task.OperationDescription);

                            //  Wait until the reset time is reached or until another request has completed
                            //  (which will update the remaining requests and reset time, possibly allowing
                            //  this request to be executed)

                            TimeSpan delayTime = resetTime.Subtract(DateTimeOffset.UtcNow);
                            if (delayTime.TotalMilliseconds < 10)
                            {
                                delayTime = TimeSpan.FromMilliseconds(10);
                            }

                            Task delayTask = Task.Delay(delayTime);
                            Task requestCompletedTask = _requestCompleted.WaitOneAsync();

                            if (await Task.WhenAny(delayTask, requestCompletedTask) == requestCompletedTask)
                            {
                                //  Reset "request completed" WaitHandle
                                //  If multiple requests are waiting, then they should all get signalled and
                                //  then they'll all Reset the WaitHandle.  It only needs to be reset once but
                                //  doing it multiple times shouldn't hurt (I think)
                                _requestCompleted.Reset();
                            }
                            else
                            {
                                lock (_lockObject)
                                {
                                    if (_remainingRequests == 0)
                                    {
                                        _remainingRequests = _limit;
                                    }
                                }
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
                _logger.Information("Running {@Operation}", task.OperationDescription);

                IRateLimit result = await task.Action();

                _logger.Information("Finished {@Operation}", task.OperationDescription);

                lock (_lockObject)
                {
                    _remainingRequests = result.RateLimit.Remaining;
                    _limit = result.RateLimit.Limit;
                    _resetTime = result.RateLimit.Reset;
                }

                task.CompletionSource.SetResult(result);
                _requestCompleted.Set();
            }
            catch (RateLimitExceededException ex)
            {
                _logger.Warning("Rate limit exceeded for {@Operation}, resets at {ResetTime}. {RemainingRequests}/{RequestLimit}",
                    task.OperationDescription, ex.Reset, ex.Remaining, ex.Limit);

                lock (_lockObject)
                {
                    _remainingRequests = ex.Remaining;
                    _limit = ex.Limit;
                    _resetTime = ex.Reset;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed {@Operation}", task.OperationDescription);
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
