﻿using System;

namespace GradeSyncApi.Services.Storage
{
    public interface IMessageQueueService
    {
        Task SendMessageGradeSyncQueue(string classId, string jobId, string tenantId);
    }
}
