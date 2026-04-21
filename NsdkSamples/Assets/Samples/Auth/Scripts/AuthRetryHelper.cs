// Copyright Niantic Spatial.

using System;
using System.Threading;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR.Auth;
using UnityEngine;

namespace NianticSpatial.NSDK.AR.Utilities
{
    /// <summary>
    /// A helper class that provides automatic retry logic for NSDK operations that may fail.
    ///
    /// Flow:
    /// 1. Before running: if not authorized, wait for authorization.
    /// 2. Run the operation.
    /// 3. If the operation fails: wait 1 second, then retry once.
    /// 4. If the retry fails, the exception is thrown (no further retries).
    ///
    /// Usage:
    /// <code>
    /// var retryHelper = new AuthRetryHelper();
    /// var result = await retryHelper.WithRetryAsync(ct => sitesClient.GetSelfUserInfoAsync(ct), cancellationToken);
    /// </code>
    /// </summary>
    public class AuthRetryHelper
    {
        private const float InitialAuthWaitSeconds = 30f;
        private const float RetryDelaySeconds = 1f;
        private const float AuthPollIntervalSeconds = 0.5f;

        /// <summary>
        /// Executes an async operation with automatic retry.
        /// Pre-flight: waits for authorization if not already authorized. Then runs the operation; on failure waits 1s and retries once. If the retry fails, the exception is thrown.
        /// </summary>
        /// <typeparam name="T">The result type</typeparam>
        /// <param name="operation">The async operation to execute, receiving a cancellation token</param>
        /// <param name="cancellationToken">Cancellation token for the entire operation including retries</param>
        /// <returns>The result of the operation</returns>
        public async Task<T> WithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            await WaitForAuthorizationIfNeededAsync(InitialAuthWaitSeconds, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                // Exit if the operation was cancelled. We could also rethrow cancellation, but during application exit
                // this results in a more graceful end (no error messages).
                return default;
            }

            try
            {
                return await operation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Exit if the operation was cancelled. We could also rethrow cancellation, but during application
                // exit this results in a more graceful end (no error messages).
                return default;
            }
            catch (Exception e)
            {
                Debug.Log($"[AuthRetryHelper] Operation failed: {e.Message}, waiting 1 second before retry...");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Another cancellation catch here, in case cancellation occured during the delay before retrying.
                    return default;
                }

                return await operation(cancellationToken);
            }
        }

        /// <summary>
        /// If not authorized, waits for the NSDK session to become authorized (pre-flight before running the operation).
        /// </summary>
        /// <param name="timeoutSeconds">Maximum time to wait in seconds</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private static async Task WaitForAuthorizationIfNeededAsync(
            float timeoutSeconds, CancellationToken cancellationToken)
        {
            float elapsed = 0f;
            while (elapsed < timeoutSeconds && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (AuthClient.IsAuthorized())
                    {
                        return;
                    }
                }
                catch (Exception)
                {
                    // If IsAuthorized fails, continue waiting
                }

                try
                {
                    await Task.Delay((int)(AuthPollIntervalSeconds * 1000), cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    // Exit if the operation was cancelled
                    return;
                }

                elapsed += AuthPollIntervalSeconds;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                Debug.LogWarning($"[AuthRetryHelper] Authorization timeout after {timeoutSeconds} seconds");                
            }
        }
    }
}
