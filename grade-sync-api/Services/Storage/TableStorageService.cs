// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Azure.Data.Tables;
using GradeSyncApi.Helpers;

namespace GradeSyncApi.Services.Storage
{
    public class TableStorageService : ITableStorageService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<TableStorageService> _logger;
        private Dictionary<string, TableClient> _tableClients;

        public TableStorageService(IConfiguration configuration, ILogger<TableStorageService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _tableClients = new Dictionary<string, TableClient>();
        }

        private async Task<TableClient> GetTableClient(string tableName)
        {
            if (_tableClients.TryGetValue(tableName, out TableClient client))
            {
                return client;
            }

            var serviceClient = new TableServiceClient(_configuration["StorageConnectionString"]);
            var tableClient = serviceClient.GetTableClient(tableName);
            await tableClient.CreateIfNotExistsAsync();

            _tableClients.Add(tableName, tableClient);
            return tableClient;
        }

        // Assignment entity implementations
        public async Task<List<AssignmentEntity>?> GetAssignmentsByClassId(string classId)
        {
            var tableClient = await GetTableClient(AssignmentEntity.TableName);
            try
            {
                var assignments = await tableClient.QueryAsync<AssignmentEntity>(x => x.PartitionKey == classId).ToListAsync();
                return assignments;
            }
            catch (Azure.RequestFailedException e)
            {
                _logger.Log(LogLevel.Error, e, classId);
                return null;
            }
        }

        public async Task<List<AssignmentEntity>?> GetAssignmentsFromIdList(string classId, List<string> assignmentIdList, bool forceSync)
        {
            var tableClient = await GetTableClient(AssignmentEntity.TableName);
            var assignments = new List<AssignmentEntity>();
            try
            {
                foreach (var id in assignmentIdList)
                {
                    var res = await tableClient.GetEntityAsync<AssignmentEntity>(classId, id);
                    var assignment = res.Value;
                    assignment.ForceSync = forceSync;
                    assignments.Add(assignment);
                }
                return assignments;
            }
            catch (Azure.RequestFailedException e)
            {
                _logger.Log(LogLevel.Error, e, classId);
                return null;
            }
        }

        public async Task BatchTransactAssignmentsAsync(List<TableTransactionAction> assignmentActions)
        {
            try
            {
                if (assignmentActions.Count > 0)
                {
                    var tableClient = await GetTableClient(AssignmentEntity.TableName);
                    await tableClient.SubmitTransactionAsync(assignmentActions);
                }
            } catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e, assignmentActions.First().Entity.PartitionKey);
            }
        }

        public async Task BatchUpdateAssignmentsForJobAsync(
            string classId,
            GradeSyncStatus status,
            JobPayloadWrapper payloadWrapper,
            string jobId,
            string? oneRosterConnectionId
            )
        {
            var idSet = new HashSet<string>(payloadWrapper.IdList);
            var assignmentsToUpdate = new List<TableTransactionAction>();

            try
            {
                var assignments = await GetAssignmentsByClassId(classId);
                foreach (var assignment in assignments!)
                {
                    if (idSet.Contains(assignment.RowKey!))
                    {
                        assignment.SyncStatus = status;
                        assignment.CurrentSyncJobId = jobId;

                        if (payloadWrapper.CategoryMap is not null && oneRosterConnectionId is not null)
                        {
                            if (payloadWrapper.CategoryMap.TryGetValue(assignment.RowKey!, out string catId))
                            {
                                assignment.AddCategoryMapping(oneRosterConnectionId, catId);
                            }
                        }

                        var action = new TableTransactionAction(TableTransactionActionType.UpsertMerge, assignment);
                        assignmentsToUpdate.Add(action);
                    }
                }

                await BatchTransactAssignmentsAsync(assignmentsToUpdate);
            }
            catch (Azure.RequestFailedException e)
            {
                _logger.Log(LogLevel.Error, e, classId);
            }
        }

        // GradeSyncJob entity implementations

        public async Task UpsertGradeSyncJobEntityAsync(GradeSyncJobEntity entity)
        {
            try
            {
                var tableClient = await GetTableClient(GradeSyncJobEntity.TableName);
                await tableClient.UpsertEntityAsync(entity);
            }
            catch (Azure.RequestFailedException e)
            {
                _logger.Log(LogLevel.Error, e, entity.PartitionKey);
            }
        }

        public async Task<GradeSyncJobEntity?> GetGradeSyncJobEntityAsync(string classId, string jobId)
        {
            try
            {
                var tableClient = await GetTableClient(GradeSyncJobEntity.TableName);
                GradeSyncJobEntity? entity = await tableClient.GetEntityAsync<GradeSyncJobEntity>(classId, jobId);
                return entity;
            }
            catch (Azure.RequestFailedException e)
            {
                _logger.Log(LogLevel.Error, e, classId);
                return null;
            }
        }

        public async Task DeleteGradeSyncJobEntityAsync(GradeSyncJobEntity entity)
        {
            try
            {
                var tableClient = await GetTableClient(GradeSyncJobEntity.TableName);
                await tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
            }
            catch (Azure.RequestFailedException e)
            {
                _logger.Log(LogLevel.Error, e, entity.RowKey);
            }
        }

        // OneRosterConnection entity implementations

        public async Task UpsertOneRosterConnectionEntityAsync(OneRosterConnectionEntity entity)
        {
            var encryptionKey = _configuration["EncryptionKey"];
            var firstEncryptionPass = AesHelper.Encrypt(entity.OneRosterBaseUrl, encryptionKey!);
            entity.OneRosterBaseUrl = firstEncryptionPass.Item1;
            entity.IVBase64 = firstEncryptionPass.Item2;

            entity.OAuth2TokenUrl = AesHelper.Encrypt(entity.OAuth2TokenUrl, encryptionKey!, entity.IVBase64).Item1;
            entity.ClientId = AesHelper.Encrypt(entity.ClientId, encryptionKey!, entity.IVBase64).Item1;
            entity.ClientSecret = AesHelper.Encrypt(entity.ClientSecret, encryptionKey!, entity.IVBase64).Item1;

            try
            {
                var tableClient = await GetTableClient(OneRosterConnectionEntity.TableName);
                await tableClient.UpsertEntityAsync(entity); 
            }
            catch (Azure.RequestFailedException e)
            {
                _logger.Log(LogLevel.Error, e, entity.RowKey);
            }
        }

        public async Task<OneRosterConnectionEntity?> GetOneRosterConnectionEntity(string tid, string connectionId)
        {
            // This method should only be used by the grade-sync-worker project since it decrypts the actual values
            var encryptionKey = _configuration["EncryptionKey"];

            try
            {
                var tableClient = await GetTableClient(OneRosterConnectionEntity.TableName);
                OneRosterConnectionEntity? entity = await tableClient.GetEntityAsync<OneRosterConnectionEntity>(tid, connectionId);

                // decrypt values
                entity.OneRosterBaseUrl = AesHelper.Decrypt(entity.OneRosterBaseUrl, encryptionKey!, entity.IVBase64!);
                entity.OAuth2TokenUrl = AesHelper.Decrypt(entity.OAuth2TokenUrl, encryptionKey!, entity.IVBase64!);
                entity.ClientId = AesHelper.Decrypt(entity.ClientId, encryptionKey!, entity.IVBase64!);
                entity.ClientSecret = AesHelper.Decrypt(entity.ClientSecret, encryptionKey!, entity.IVBase64!);
                return entity;
            }
            catch (Azure.RequestFailedException e)
            {
                _logger.Log(LogLevel.Error, e, $"OneRoster connection not found with ID: {connectionId} Check to see if an admin deleted it.");
                return null;
            }
        }

        public async Task<List<OneRosterConnectionDto>?> GetOneRosterConnectionDtos(string tid, string userId, bool isAdmin)
        {
            var tableClient = await GetTableClient(OneRosterConnectionEntity.TableName);
            try
            {
                var connections = await tableClient.QueryAsync<OneRosterConnectionEntity>(x => x.PartitionKey == tid).ToListAsync();
                var userSettings = await GetUserSettingsEntityAsync(tid, userId);
                var dtoList = new List<OneRosterConnectionDto>();

                foreach (var connection in connections)
                {
                    var isDefault = false;
                    if (userSettings?.DefaultOneRosterConnectionId is not null)
                    {
                        if (userSettings.DefaultOneRosterConnectionId == connection.RowKey) isDefault = true;
                    }

                    var dto = new OneRosterConnectionDto(connection, isAdmin, isDefault);
                    dtoList.Add(dto);
                }
                return dtoList;
            }
            catch (Azure.RequestFailedException e)
            {
                _logger.Log(LogLevel.Error, e, tid);
                return null;
            }
        }

        public async Task DeleteOneRosterConnectionAsync(OneRosterConnectionEntity entity)
        {
            try
            {
                var tableClient = await GetTableClient(OneRosterConnectionEntity.TableName);
                await tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
            }
            catch (Azure.RequestFailedException e)
            {
                _logger.Log(LogLevel.Error, e, entity.RowKey);
            }
        }

        // UserSettingsEntity implementations
        public async Task UpsertUserSettingsEntityAsync(UserSettingsEntity entity)
        {
            try
            {
                var tableClient = await GetTableClient(UserSettingsEntity.TableName);
                await tableClient.UpsertEntityAsync(entity);
            }
            catch (Azure.RequestFailedException e)
            {
                _logger.Log(LogLevel.Error, e, entity.RowKey);
            }
        }

        public async Task<UserSettingsEntity?> GetUserSettingsEntityAsync(string tid, string userId)
        {
            try
            {
                var tableClient = await GetTableClient(UserSettingsEntity.TableName);
                UserSettingsEntity? entity = await tableClient.GetEntityAsync<UserSettingsEntity>(tid, userId);
                return entity;
            }
            catch (Azure.RequestFailedException e)
            {
                _logger.Log(LogLevel.Error, e, userId);
                return null;
            }
        }
    }       
}
