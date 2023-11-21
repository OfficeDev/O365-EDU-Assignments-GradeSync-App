// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Azure;
using Azure.Data.Tables;

namespace GradeSyncApi.Services.Storage
{
    public abstract class BaseEntity : ITableEntity
    {
        public string? PartitionKey { get; set; }
        public string? RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

