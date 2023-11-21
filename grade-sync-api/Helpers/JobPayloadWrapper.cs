// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Newtonsoft.Json;

namespace GradeSyncApi.Helpers
{
    public class JobPayloadWrapper
    {
        public JobPayloadWrapper(List<string> idList)
        {
            IdList = idList;
        }

        [JsonProperty("idList")]
        public List<string> IdList { get; set; }

        [JsonProperty("categoryMap")]
        public Dictionary<string, string>? CategoryMap { get; set; }
    }
}

