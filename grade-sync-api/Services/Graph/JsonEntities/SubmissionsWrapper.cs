using System;
using Newtonsoft.Json;
using GradeSyncApi.Services.Storage;

namespace GradeSyncApi.Services.Graph.JsonEntities
{
    public class SubmissionsWrapper
    {
        public SubmissionsWrapper()
        {
        }

        [JsonProperty("value")]
        public List<SubmissionEntity> Submissions { get; set; }
    }
}

