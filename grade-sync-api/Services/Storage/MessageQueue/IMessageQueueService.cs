// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace GradeSyncApi.Services.Storage
{
    public interface IMessageQueueService
    {
        Task SendMessageGradeSyncQueue(string classId, string jobId, string tenantId);
    }
}

