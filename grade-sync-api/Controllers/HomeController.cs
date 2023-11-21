// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;


using GradeSyncApi.Helpers;
using GradeSyncApi.Services.Graph;
using GradeSyncApi.Services.Storage;
using GradeSyncApi.Services.OneRoster;

namespace GradeSyncApi.Controllers
{
    [ApiController]
    [Authorize]
    public class HomeController : ControllerBase
    {
        private readonly ITableStorageService _storageService;
        private readonly IGraphService _graphService;
        private readonly IMessageQueueService _messageQueueService;
        private readonly IOneRosterService _oneRosterService;

        public HomeController(
            ITableStorageService storageService,
            IGraphService graphService,
            IMessageQueueService messageQueueService,
            IOneRosterService oneRoster)
        {
            _storageService = storageService;
            _graphService = graphService;
            _messageQueueService = messageQueueService;
            _oneRosterService = oneRoster;
        }

        [HttpGet]
        [Route("api/get-assignments-by-class/{classId}")]
        public async Task<IActionResult> GetAssignmentsByClass(string classId)
        {
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);
            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);

            var authorized = await _graphService.CanAccessClass(classId);
            if (authorized)
            {
                var graphAssignments = await _graphService.GetAssignmentsByClass(classId);
                if (graphAssignments is null) return NotFound();

                var storedAssignments = await _storageService.GetAssignmentsByClassId(classId);
                var syncHelper = new AssignmentSyncHelper(graphAssignments, storedAssignments);
                syncHelper.SyncState();

                await _storageService.BatchTransactAssignmentsAsync(syncHelper.AssignmentsToUpsert);
                await _storageService.BatchTransactAssignmentsAsync(syncHelper.AssignmentsToDelete);
                return Ok(syncHelper.GetModifiedGraphAssignments());
            }
            else return Unauthorized();
        }

        [HttpPost]
        [Route("api/queue-gradesync/{classId}/{connectionId}/{forceSync}")]
        public async Task<IActionResult> EnqueueGradeSync(string classId, string connectionId, bool forceSync, JobPayloadWrapper wrapper)
        {
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);
            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);
            
            var authorized = await _graphService.CanAccessClass(classId);
            if (authorized)
            {
                var jobEntity = new GradeSyncJobEntity
                {
                    PartitionKey = classId, // partition sync jobs by classId
                    RowKey = Guid.NewGuid().ToString(), // create guid for this sync job
                    JobStatus = GradeSyncJobStatus.Queued,
                    OneRosterConnectionId = connectionId,
                    ForceSync = forceSync
                };
                jobEntity.SerializeIdList(wrapper.IdList);
                
                var teamsClass = await _graphService.GetClass(classId);
                jobEntity.ClassExternalId = teamsClass!.ExternalId;
                await _storageService.UpsertGradeSyncJobEntityAsync(jobEntity);

                await _storageService.BatchUpdateAssignmentsForJobAsync(classId, GradeSyncStatus.InProgress, wrapper, jobEntity.RowKey, connectionId);
                await _messageQueueService.SendMessageGradeSyncQueue(classId, jobEntity.RowKey, claims["tid"]);

                return Ok(jobEntity.RowKey);
            }
            else return Unauthorized();
        }

        [HttpGet]
        [Route("api/cancel-gradesync/{classId}/{jobId}")]
        public async Task<IActionResult> CancelGradeSyncJob(string classId, string jobId)
        {
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);
            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);

            var authorized = await _graphService.CanAccessClass(classId);
            if (authorized)
            {
                var job = await _storageService.GetGradeSyncJobEntityAsync(classId, jobId);
                if (job is null) return BadRequest();

                job.JobStatus = GradeSyncJobStatus.Cancelled;
                await _storageService.UpsertGradeSyncJobEntityAsync(job);

                job.DeserializeIdList();
                await _storageService.BatchUpdateAssignmentsForJobAsync(
                    classId,
                    GradeSyncStatus.Cancelled,
                    new JobPayloadWrapper(job.AssignmentIdList),
                    job.RowKey!,
                    null!
                );

                return Ok();
            }
            else return Unauthorized();
        }

        [HttpGet]
        [Route("api/get-one-roster-connections/{isAdmin}")]
        public async Task<IActionResult> GetOneRosterConnections(bool isAdmin)
        {
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);
            var connections = await _storageService.GetOneRosterConnectionDtos(claims["tid"], claims["sub"], isAdmin);
            return Ok(connections);
        }

        [HttpGet]
        [Route("api/is-admin")]
        public async Task<IActionResult> IsAdmin()
        {
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);
            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);
            return Ok(await _graphService.HasAdminRole());
        }

        [HttpGet]
        [Route("api/is-student-role")]
        public async Task<IActionResult> GetEduRole()
        {
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);
            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);

            var student = await _graphService.IsStudentPrimaryRole();
            return Ok(student);
        }

        [HttpGet]
        [Route("api/get-job-status/{classId}/{jobId}")]
        public async Task<IActionResult> GetJobStatus(string classId, string jobId)
        {
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);
            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);

            var authorized = await _graphService.CanAccessClass(classId);
            if (!authorized) return Unauthorized();

            var job = await _storageService.GetGradeSyncJobEntityAsync(classId, jobId);
            if (job is null) return BadRequest();
            
            return Ok(job.JobStatus);
        }

        [HttpPost]
        [Route("api/set-default-connection/{connectionId}")]
        public async Task<IActionResult> SetDefaultOneRosterConnection(string connectionId)
        {
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);
            var connection = await _storageService.GetOneRosterConnectionEntity(claims["tid"], connectionId);
            if (connection is null) return BadRequest();

            var userSettings = await _storageService.GetUserSettingsEntityAsync(claims["tid"], claims["sub"]);
            if (userSettings is null)
            {
                userSettings = new UserSettingsEntity
                {
                    PartitionKey = claims["tid"],
                    RowKey = claims["sub"],
                    DefaultOneRosterConnectionId = connectionId
                };
            } else
            {
                userSettings.DefaultOneRosterConnectionId = connectionId;
            }

            await _storageService.UpsertUserSettingsEntityAsync(userSettings);
            return Ok();
        }

        [HttpGet]
        [Route("api/default-connection-id")]
        public async Task<IActionResult> CheckDefaultOneRosterConnection()
        {
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);
            var connectionDtos = await _storageService.GetOneRosterConnectionDtos(claims["tid"], claims["sub"], false);
            if (connectionDtos is null || connectionDtos.Count == 0) return BadRequest();

            foreach (var dto in connectionDtos)
            {
                if (dto.IsDefaultConnection) return Ok(dto.ConnectionId);
            }

            // if user doesn't have a default connection and there is only one available on the tenant, set it as their default for them
            if (connectionDtos.Count == 1)
            {
                var connectionId = connectionDtos!.FirstOrDefault()!.ConnectionId;
                var userSettings = new UserSettingsEntity
                {
                    PartitionKey = claims["tid"],
                    RowKey = claims["sub"],
                    DefaultOneRosterConnectionId = connectionId
                };

                await _storageService.UpsertUserSettingsEntityAsync(userSettings);
                return Ok(connectionId);
            }

            // at this point we just return bad request because they don't have a default connection, and there is more than one available on the tenant so
            // we can't assume and set it for them
            return BadRequest();
        }

        [HttpGet]
        [Route("api/line-item-categories/{connectionId}")]
        public async Task<IActionResult> GetLineItemCategoriesByConnectionId(string connectionId)
        {
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);
            var connection = await _storageService.GetOneRosterConnectionEntity(claims["tid"], connectionId);
            if (connection is null) return BadRequest();

            try
            {
                await _oneRosterService.InitApiConnection(connection);
                var categories = await _oneRosterService.GetActiveCategories();
                return Ok(categories);
            } catch (Exception e)
            {
                return BadRequest(e.Message);
            }
        }

        [HttpPost]
        [Route("api/create-one-roster-connection")]
        public async Task<IActionResult> CreateOneRosterConnection(OneRosterConnectionEntity connectionEntity)
        {
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);
            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);
            var isAdmin = await _graphService.HasAdminRole();
            if (!isAdmin) return Unauthorized();

            try
            {
                // validate that the connection is valid before we save it
                connectionEntity.OneRosterBaseUrl = connectionEntity.OneRosterBaseUrl.TrimEnd('/');
                connectionEntity.OAuth2TokenUrl = connectionEntity.OAuth2TokenUrl.TrimEnd('/');
                await _oneRosterService.InitApiConnection(connectionEntity);

                if (connectionEntity.IsGroupEnabled)
                {
                    await _oneRosterService.ValidateClassGroups();
                }
            }
            catch (ApplicationException e) { return BadRequest(e.Message); }

            if (connectionEntity.EditConnectionId != "")
            {
                // ability to edit existing connection. we just want to find it by id and overwrite every property except the ID's
                var storedConnection = await _storageService.GetOneRosterConnectionEntity(claims["tid"], connectionEntity.EditConnectionId!);
                if (storedConnection is null) return BadRequest();

                storedConnection.DisplayName = connectionEntity.DisplayName;
                storedConnection.OneRosterBaseUrl = connectionEntity.OneRosterBaseUrl;
                storedConnection.OAuth2TokenUrl = connectionEntity.OAuth2TokenUrl;
                storedConnection.ClientId = connectionEntity.ClientId;
                storedConnection.ClientSecret = connectionEntity.ClientSecret;
                storedConnection.IsGroupEnabled = connectionEntity.IsGroupEnabled;
                await _storageService.UpsertOneRosterConnectionEntityAsync(storedConnection);
            }
            else
            {
                connectionEntity.PartitionKey = claims["tid"];
                connectionEntity.RowKey = Guid.NewGuid().ToString();
                connectionEntity.CreatedByUserId = claims["sub"];
                await _storageService.UpsertOneRosterConnectionEntityAsync(connectionEntity);
            }

            return Ok(connectionEntity.RowKey);
        }

        [HttpPost]
        [Route("api/delete-connection/{connectionId}")]
        public async Task<IActionResult> DeleteOneRosterConnection(string connectionId)
        {
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);
            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);
            var isAdmin = await _graphService.HasAdminRole();
            if (!isAdmin) return Unauthorized();

            var connection = await _storageService.GetOneRosterConnectionEntity(claims["tid"], connectionId);
            if (connection is null) return BadRequest();

            await _storageService.DeleteOneRosterConnectionAsync(connection);
            return Ok();
        }
    }
}
