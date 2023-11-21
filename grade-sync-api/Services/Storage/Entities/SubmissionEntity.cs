// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;

namespace GradeSyncApi.Services.Storage
{
    [JsonObject(MemberSerialization.OptIn)]
    public class SubmissionEntity
    {
        public SubmissionEntity()
        {
        }

        [JsonProperty("id")]
        public string SubmissionId { get; set; }

        [JsonProperty("status")]
        public SubmissionStatus SubmissionStatus { get; set; } // one of: working, submitted, returned

        [JsonProperty("submittedDateTime")]
        public string SubmittedDateTime { get; set; }

        [JsonProperty("returnedDateTime")]
        public string ReturnedDateTime { get; set; }

        [JsonProperty("submittedBy")]
        public SubmittedBy SubmittedByUser { get; set; }

        [JsonProperty("outcomes")]
        public List<Outcome> Outcomes { get; set; }
    }

    public class SubmittedBy
    {
        [JsonProperty("user")]
        public User User { get; set; }
    }

    public class User
    {
        [JsonProperty("id")]
        public string Id { get; set; }
    }

    public class Outcome
    {
        [JsonProperty("@odata.type")]
        public OutcomeType OutcomeType { get; set; }

        [JsonProperty("points")]
        public AssignmentPointsGrade Points { get; set; }

        [JsonProperty("publishedPoints")]
        public AssignmentPointsGrade PublishedPoints { get; set; }
    }

    public class AssignmentPointsGrade
    {
        [JsonProperty("points")]
        public double? Points { get; set; }
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum SubmissionStatus
    {
        [EnumMember(Value = "working")]
        Working,
        [EnumMember(Value = "submitted")]
        Submitted,
        [EnumMember(Value = "returned")]
        Returned
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum OutcomeType
    {
        [EnumMember(Value = "#microsoft.graph.educationPointsOutcome")]
        PointsOutcome,
        [EnumMember(Value = "#microsoft.graph.educationFeedbackOutcome")]
        FeedbackOutcome,
        [EnumMember(Value = "#microsoft.graph.educationRubricOutcome")]
        RubricOutcome,
    }
}

