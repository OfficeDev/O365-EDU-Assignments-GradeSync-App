using GradeSyncApi.Services.Storage;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using Moq;
using Xunit;

namespace GradeSyncTest;

public class TableService_Test
{
    public TableStorageService GetService()
    {
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("secrets.test.json")
            .Build();

        var mock = new Mock<ILogger<TableStorageService>>();
        var logger = mock.Object;
        var tableStorageService = new TableStorageService(config, logger);
        return tableStorageService;
    }

    [Fact]
    public async void CanCRUDGradeSyncJobEntity()
    {
        var service = GetService();
        var classId = Guid.NewGuid().ToString();
        var jobId = Guid.NewGuid().ToString();

        var entity = new GradeSyncJobEntity();
        entity.PartitionKey = classId;
        entity.RowKey = jobId;
        entity.JobStatus = GradeSyncJobStatus.Queued;

        await service.UpsertGradeSyncJobEntityAsync(entity);
        var fetchedEntity = await service.GetGradeSyncJobEntityAsync(classId, jobId);

        if (fetchedEntity is not null)
        {
            Assert.Equal(GradeSyncJobStatus.Queued, fetchedEntity.JobStatus);
        }

        entity.JobStatus = GradeSyncJobStatus.InProgress;
        await service.UpsertGradeSyncJobEntityAsync(entity);
        fetchedEntity = await service.GetGradeSyncJobEntityAsync(classId, jobId);

        if (fetchedEntity is not null)
        {
            Assert.Equal(GradeSyncJobStatus.InProgress, fetchedEntity.JobStatus);
        }

        await service.DeleteGradeSyncJobEntityAsync(entity);
        var deleted = await service.GetGradeSyncJobEntityAsync(classId, jobId);
        Assert.Null(deleted);
    }
}
