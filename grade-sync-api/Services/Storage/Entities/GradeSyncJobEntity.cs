// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace GradeSyncApi.Services.Storage
{
    public enum GradeSyncJobStatus
    {
        Queued,
        InProgress,
        Finished,
        Cancelled
    }

    public class GradeSyncJobEntity : BaseEntity
    {
        public GradeSyncJobEntity() { }

        public static string TableName = "GradeSyncJobs";

        public string ClassExternalId { get; set; }

        public string SerializedAssignmentIdList { get; set; }

        public GradeSyncJobStatus JobStatus { get; set; }

        public string OneRosterConnectionId { get; set; }

        public bool ForceSync { get; set; }

        [JsonIgnore]
        [IgnoreDataMember]
        public List<string> AssignmentIdList { get; set; }

        [JsonIgnore]
        [IgnoreDataMember]
        public List<AssignmentEntity>? AssignmentEntities { get; set; }

        [JsonIgnore]
        [IgnoreDataMember]
        public Dictionary<string, List<SubmissionEntity>> AssignmentsWithSubmissions { get; set; }

        [JsonIgnore]
        [IgnoreDataMember]
        public OneRosterConnectionEntity ConnectionEntity { get; set; }

        [JsonIgnore]
        [IgnoreDataMember]
        public Dictionary<string, string> StudentSisIdDict { get; set; }

        [JsonIgnore]
        [IgnoreDataMember]
        public Dictionary<string, List<string>>? ClassGroupEnrollmentsUserMap { get; set; }

        [JsonIgnore]
        [IgnoreDataMember]
        // for groupEnabled connections, this is the list of subclasses under the parent Teams class
        // in the corresponding OneRoster environment
        public List<string> ListSubclassExternalIds { get; set; }

        public void SerializeIdList(List<string> idList)
        {
            SerializedAssignmentIdList = string.Join(":", idList);
        }

        public void DeserializeIdList()
        {
            AssignmentIdList = SerializedAssignmentIdList.Split(":").ToList();
        }
    }

    public class ClassEntity
    {
        public ClassEntity(string externalId)
        {
            ExternalId = externalId;
        }

        [JsonProperty("id")]
        public string ClassId { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("externalId")]
        public string ExternalId { get; set; }
    }

    public class AssignmentWithSubmissions
    {
        public AssignmentWithSubmissions() { }

        public string AssignmentId { get; set; }
        public List<SubmissionEntity> Submissions { get; set; }
    }
}

