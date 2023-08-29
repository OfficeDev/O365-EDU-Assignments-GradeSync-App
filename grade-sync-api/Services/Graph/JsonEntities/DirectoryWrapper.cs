using System;
using Newtonsoft.Json;

namespace GradeSyncApi.Services.Graph.JsonEntities
{
    public class DirectoryWrapper
    {
        public DirectoryWrapper() {}

        [JsonProperty("value")]
        public List<DirectoryRole> Roles { get; set; }
    }

    public class DirectoryRole
    {
        [JsonProperty("@odata.type")]
        public string GraphType { get; set; }

        public string DisplayName { get; set; }

        public string RoleTemplateId { get; set; }
    }
}

