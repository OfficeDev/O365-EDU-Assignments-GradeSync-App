using System;
using Newtonsoft.Json;
using GradeSyncApi.Services.Storage;

namespace GradeSyncApi.Services.Graph.JsonEntities
{
    public class AssignmentsWrapper
    {
        public AssignmentsWrapper()
        {
        }

        [JsonProperty("value")]
        public List<AssignmentEntity> Assignments { get; set; }
    }
}

