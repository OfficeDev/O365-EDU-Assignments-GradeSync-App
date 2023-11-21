// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

using GradeSyncApi.Services.Storage;
using GradeSyncApi.Services.Graph;
using GradeSyncApi.Services.OneRoster;

using grade_sync_worker.Helpers;

namespace grade_sync_worker
{
    public class GradeSync
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;
        private readonly ITableStorageService _storageService;
        private readonly IGraphService _graphService;
        private readonly IOneRosterService _oneRosterService;

        public GradeSync(
            ILoggerFactory loggerFactory,
            ITableStorageService storageService,
            IGraphService graphService,
            IOneRosterService oneRoster,
            IConfiguration configuration)
        {
            _logger = loggerFactory.CreateLogger<GradeSync>();
            _storageService = storageService;
            _graphService = graphService;
            _oneRosterService = oneRoster;
            _configuration = configuration;
        }

        [Function("RunGradeSync")]
        public async Task Run([QueueTrigger("one-roster-gradesync", Connection = "StorageConnectionString")] string queueMessage)
        {
            GradeSyncQueueMessage? gradeSyncMessage = null;
            GradeSyncJobEntity? gradeSyncJobEntity = null;

            try
            {
                gradeSyncMessage = JsonConvert.DeserializeObject<GradeSyncQueueMessage>(queueMessage);
                gradeSyncJobEntity = await _storageService.GetGradeSyncJobEntityAsync(gradeSyncMessage!.ClassId, gradeSyncMessage.JobId);
                if (gradeSyncJobEntity is not null)
                {
                    gradeSyncJobEntity.DeserializeIdList();
                    // get assignment entities
                    gradeSyncJobEntity.AssignmentEntities =
                        await _storageService.GetAssignmentsFromIdList(gradeSyncMessage.ClassId, gradeSyncJobEntity.AssignmentIdList, gradeSyncJobEntity.ForceSync);
                }
            } catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e, queueMessage);
            }
            
            try
            {
                // make additional state checks to ensure job isn't going to fail for missing required pieces 
                if (gradeSyncJobEntity is null) throw new InvalidOperationException("GradeSyncJobEntity does not exist.");
                if (gradeSyncJobEntity.JobStatus == GradeSyncJobStatus.Cancelled) throw new ApplicationException("Job cancelled by user");
                if (gradeSyncJobEntity.AssignmentIdList is null || gradeSyncJobEntity.AssignmentIdList.Count == 0 || gradeSyncJobEntity.SerializedAssignmentIdList == "")
                {
                    throw new InvalidOperationException("Sync job was created in a malformed state and has no assignments to sync.");
                }
                if (gradeSyncJobEntity.ClassExternalId is null || gradeSyncJobEntity.ClassExternalId == "")
                {
                    throw new InvalidOperationException("Teams EDU class does not have an external/SIS ID set. Contact your administrator or run Microsoft SDS to sync your classes.");
                }

                var connectionEntity = await _storageService.GetOneRosterConnectionEntity(gradeSyncMessage!.TenantId, gradeSyncJobEntity.OneRosterConnectionId);
                if (connectionEntity is null) throw new InvalidOperationException("OneRoster API connection does not exist or was deleted by owner.");
                gradeSyncJobEntity.ConnectionEntity = connectionEntity;

                // make additional graph calls for data
                await _graphService.ExchangeGraphTokenClientCredentials(gradeSyncMessage.TenantId, true);
                gradeSyncJobEntity.AssignmentsWithSubmissions =
                    await _graphService.GetAllAssignmentsWithSubmissions(gradeSyncMessage.ClassId, gradeSyncJobEntity.AssignmentIdList);

                gradeSyncJobEntity.StudentSisIdDict = await _graphService.GetStudentSisIdDict(gradeSyncMessage.ClassId);

                await _oneRosterService.InitApiConnection(gradeSyncJobEntity.ConnectionEntity);
                await _oneRosterService.GetCurrentGradingPeriod();
                if (gradeSyncJobEntity.ConnectionEntity.IsGroupEnabled)
                {
                    gradeSyncJobEntity.ClassGroupEnrollmentsUserMap = await OneRosterSyncHelper.GetClassGroupEnrollmentsUserMap(gradeSyncJobEntity, _oneRosterService);
                    if (gradeSyncJobEntity.ClassGroupEnrollmentsUserMap is null)
                    {
                        throw new InvalidOperationException("OneRoster API connection is group-enabled but classGroups are empty. Check API response for classGroups and enrollments in each subclass");
                    }
                }

                // run all sync logic
                await OneRosterSyncHelper.UpsertAllLineItems(_oneRosterService, gradeSyncJobEntity, _configuration);
                await OneRosterSyncHelper.UpsertAllSubmissions(_oneRosterService, gradeSyncJobEntity);

                await OneRosterSyncHelper.AssignmentStatusUpdateAndResync(gradeSyncJobEntity, _storageService);
                await OneRosterSyncHelper.MarkJobComplete(gradeSyncJobEntity, _storageService);
            }
            catch (Exception e)
            {
                if (gradeSyncJobEntity?.AssignmentEntities is not null)
                {
                    await OneRosterSyncHelper.MarkJobCompleteAndAssignmentsFailed(gradeSyncJobEntity, _storageService);
                }
                else if (gradeSyncJobEntity is not null)
                {
                    await OneRosterSyncHelper.MarkJobComplete(gradeSyncJobEntity, _storageService);
                }

                _logger.Log(LogLevel.Error, e, queueMessage);
            }
        }
    }
}
