using System;
using Newtonsoft.Json;

namespace GradeSyncApi.Services.Storage
{
    public class UserSettingsEntity :BaseEntity
    {
        public UserSettingsEntity() {}

        public static string TableName = "UserSettings";

        public string DefaultOneRosterConnectionId { get; set; }
    }
}

