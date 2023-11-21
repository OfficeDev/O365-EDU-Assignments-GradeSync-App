// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Azure.Data.Tables;
using GradeSyncApi.Helpers;

namespace GradeSyncApi.Services.Storage
{
    public interface ITableStorageService
    {
        // Assignment entity methods
        Task<List<AssignmentEntity>?> GetAssignmentsByClassId(string classId);
        Task BatchTransactAssignmentsAsync(List<TableTransactionAction> assignmentActions);
        Task BatchUpdateAssignmentsForJobAsync(string classId, GradeSyncStatus status, JobPayloadWrapper payloadWrapper, string jobId, string oneRosterConnectionId);
        Task<List<AssignmentEntity>?> GetAssignmentsFromIdList(string classId, List<string> assignmentIdList, bool forceSync);

        // GradeSync job entity methods
        Task UpsertGradeSyncJobEntityAsync(GradeSyncJobEntity entity);
        Task<GradeSyncJobEntity?> GetGradeSyncJobEntityAsync(string classId, string jobId);
        Task DeleteGradeSyncJobEntityAsync(GradeSyncJobEntity entity);

        // OneRosterConnection entity methods
        Task UpsertOneRosterConnectionEntityAsync(OneRosterConnectionEntity entity);
        Task<OneRosterConnectionEntity?> GetOneRosterConnectionEntity(string tid, string connectionId);
        Task<List<OneRosterConnectionDto>?> GetOneRosterConnectionDtos(string tid, string userId, bool isAdmin);
        Task DeleteOneRosterConnectionAsync(OneRosterConnectionEntity entity);

        // UserSettings entity methods
        Task UpsertUserSettingsEntityAsync(UserSettingsEntity entity);
        Task<UserSettingsEntity?> GetUserSettingsEntityAsync(string tid, string userId);
    }
}

