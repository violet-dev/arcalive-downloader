// This source code is a part of project violet-server.
// Copyright (C) 2020. violet-team. Licensed under the MIT Licence.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace hsync.Network
{
    /// <summary>
    /// Download Queue Implementation
    /// </summary>
    public class NetQueue
    {
        SemaphoreSlim semaphore;

        public NetQueue(int count)
        {
            ThreadPool.SetMinThreads(count, count);
            semaphore = new SemaphoreSlim(count, count);
        }

        public Task Add(NetTask task)
        {
            return Task.Run(async () =>
            {
                await semaphore.WaitAsync().ConfigureAwait(false);
                _ = Task.Run(() =>
                {
                    NetField.Do(task);
                    semaphore.Release();
                }).ConfigureAwait(false);
            });
        }
    }
}
