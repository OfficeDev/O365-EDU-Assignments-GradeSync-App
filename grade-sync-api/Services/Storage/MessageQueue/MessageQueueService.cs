// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Azure.Storage.Queues;
using Newtonsoft.Json;

namespace GradeSyncApi.Services.Storage
{
    public class MessageQueueService : IMessageQueueService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<MessageQueueService> _logger;

        public MessageQueueService(IConfiguration configuration, ILogger<MessageQueueService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        private async Task<QueueClient> GetQueueClient(string queueName)
        {
            var client = new QueueClient(_configuration["StorageConnectionString"], queueName, new QueueClientOptions
            {
                MessageEncoding = QueueMessageEncoding.Base64
            });

            try
            {
                await client.CreateAsync();
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e, queueName);
            }

            return client;
        }

        public async Task SendMessageGradeSyncQueue(string classId, string jobId, string tenantId)
        {
            try
            {
                var client = await GetQueueClient("one-roster-gradesync");
                var queueMessage = new GradeSyncQueueMessage(classId, jobId, tenantId);
                await client.SendMessageAsync(JsonConvert.SerializeObject(queueMessage));
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e, jobId);
            }
        }
    }

    public class GradeSyncQueueMessage
    {
        public GradeSyncQueueMessage(string classId, string jobId, string tenantId)
        {
            ClassId = classId;
            JobId = jobId;
            TenantId = tenantId;
        }

        [JsonProperty("classId")]
        public string ClassId { get; set; }

        [JsonProperty("jobId")]
        public string JobId { get; set; }

        [JsonProperty("tenantId")]
        public string TenantId { get; set; }
    }
}
