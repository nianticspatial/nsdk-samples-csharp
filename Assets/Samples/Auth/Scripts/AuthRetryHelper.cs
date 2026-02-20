// Copyright Niantic Spatial.

using System;
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
    /// var result = await retryHelper.WithRetryAsync(() => sitesClient.GetSelfUserInfoAsync());
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
        /// <param name="operation">The async operation to execute</param>
        /// <returns>The result of the operation</returns>
        public async Task<T> WithRetryAsync<T>(Func<Task<T>> operation)
        {
            await WaitForAuthorizationIfNeededAsync(InitialAuthWaitSeconds);
            try
            {
                return await operation();
            }
            catch (Exception e)
            {
                Debug.Log($"[AuthRetryHelper] Operation failed: {e.Message}, waiting 1 second before retry...");
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds));
                return await operation();
            }
        }

        /// <summary>
        /// If not authorized, waits for the NSDK session to become authorized (pre-flight before running the operation).
        /// </summary>
        /// <param name="timeoutSeconds">Maximum time to wait in seconds</param>
        private static async Task WaitForAuthorizationIfNeededAsync(float timeoutSeconds)
        {
            float elapsed = 0f;
            while (elapsed < timeoutSeconds)
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

                await Task.Delay((int)(AuthPollIntervalSeconds * 1000));
                elapsed += AuthPollIntervalSeconds;
            }

            Debug.LogWarning($"[AuthRetryHelper] Authorization timeout after {timeoutSeconds} seconds");
        }
    }
}
