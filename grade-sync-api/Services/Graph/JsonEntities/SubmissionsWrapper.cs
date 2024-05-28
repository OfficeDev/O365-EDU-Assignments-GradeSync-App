// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using Newtonsoft.Json;
using GradeSyncApi.Services.Storage;

namespace GradeSyncApi.Services.Graph.JsonEntities
{
    public class SubmissionsWrapper
    {
        public SubmissionsWrapper() {}

        [JsonProperty("@odata.nextLink")]
        public string? NextLink { get; set; }

        [JsonProperty("value")]
        public List<SubmissionEntity> Submissions { get; set; }
    }
}
