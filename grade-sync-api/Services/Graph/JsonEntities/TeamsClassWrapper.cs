using System;
using GradeSyncApi.Services.Storage;
using Newtonsoft.Json;

namespace GradeSyncApi.Services.Graph.JsonEntities
{
    public class TeamsClassWrapper
    {
        public TeamsClassWrapper() {}

        [JsonProperty("value")]
        public List<TeamsClass> TeamsClasses { get; set; }
    }

    public class TeamsClass
    {
        public TeamsClass() {}

        [JsonProperty("id")]
        public string ClassId { get; set; }
    }
}
