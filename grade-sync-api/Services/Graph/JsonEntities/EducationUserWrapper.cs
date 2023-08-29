using System;
using Newtonsoft.Json;

namespace GradeSyncApi.Services.Graph.JsonEntities
{
    public class EducationUserWrapper
    {
        public EducationUserWrapper() {}

        [JsonProperty("value")]
        public List<EducationUser> Users { get; set; }
    }

    public class EducationUser
    {
        public EducationUser(string externalId, bool isTeacher)
        {
            if (isTeacher)
            {
                Teacher = new EducationTeacher(externalId);
            }
            else
            {
                Student = new EducationStudent(externalId);
            }
        }

        [JsonProperty("id")]
        public string UserId { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("userPrincipalName")]
        public string Email { get; set; }

        [JsonProperty("primaryRole")]
        public string Role { get; set; }

        [JsonProperty("userType")]
        public string UserType { get; set; }

        [JsonProperty("student")]
        public EducationStudent Student { get; set; }

        [JsonProperty("teacher")]
        public EducationTeacher Teacher { get; set; }
    }

    public class EducationStudent
    {
        public EducationStudent(string externalId)
        {
            ExternalId = externalId;
        }

        [JsonProperty("externalId")]
        public string ExternalId { get; set; }
    }

    public class EducationTeacher
    {
        public EducationTeacher(string externalId)
        {
            ExternalId = externalId;
        }

        [JsonProperty("externalId")]
        public string ExternalId { get; set; }
    }
}

