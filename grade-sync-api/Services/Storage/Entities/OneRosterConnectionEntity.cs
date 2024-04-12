// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Text.Json.Serialization;
using GradeSyncApi.Services.OneRoster;
using Newtonsoft.Json;

namespace GradeSyncApi.Services.Storage
{
    [JsonObject(MemberSerialization.OptIn)]
    public class OneRosterConnectionEntity : BaseEntity
    {
        public OneRosterConnectionEntity() {}

        public static string TableName = "OneRosterConnections";

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        public string? CreatedByUserId { get; set; }

        [JsonProperty("baseUrl")]
        [JsonPropertyName("baseUrl")]
        public string OneRosterBaseUrl { get; set; }

        [JsonProperty("tokenUrl")]
        [JsonPropertyName("tokenUrl")]
        public string OAuth2TokenUrl { get; set; }

        [JsonProperty("clientId")]
        public string ClientId { get; set; }

        [JsonProperty("clientSecret")]
        public string ClientSecret { get; set; }

        [JsonProperty("isGroupEnabled")]
        [JsonPropertyName("isGroupEnabled")]
        public bool IsGroupEnabled { get; set; }

        [JsonProperty("allowNoneLineItemCategory")]
        [JsonPropertyName("allowNoneLineItemCategory")]
        public bool AllowNoneLineItemCategory { get; set; }

        [JsonProperty("defaultLineItemCategory")]
        [JsonPropertyName("defaultLineItemCategory")]
        public string? DefaultLineItemCategory {  get; set; }

        public string? IVBase64 { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public string? EditConnectionId { get; set; }
    }

    public class OneRosterConnectionDto
    {
        public OneRosterConnectionDto(OneRosterConnectionEntity entity, bool canEdit, bool isDefault, List<Category>? categories)
        {
            DisplayName = entity.DisplayName;
            ConnectionId = entity.RowKey!;
            CanEdit = canEdit;
            IsDefaultConnection = isDefault;
            IsGroupEnabled = entity.IsGroupEnabled;
            AllowNoneLineItemCategory = entity.AllowNoneLineItemCategory;
            DefaultLineItemCategory = entity.DefaultLineItemCategory;
            Categories = categories;
        }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("connectionId")]
        public string ConnectionId { get; set; }
        
        [JsonProperty("canEdit")]
        public bool CanEdit { get; set; }

        [JsonProperty("isDefaultConnection")]
        public bool IsDefaultConnection { get; set; }

        [JsonProperty("isGroupEnabled")]
        public bool IsGroupEnabled { get; set; }

        [JsonProperty("allowNoneLineItemCategory")]
        public bool AllowNoneLineItemCategory { get; set; }

        [JsonProperty("defaultLineItemCategory")]
        public string? DefaultLineItemCategory { get; set; }

        [JsonProperty("categories")]
        public List<Category>? Categories { get; set; }
    }

    public class ConnectionDetailsDto
    {
        public ConnectionDetailsDto() { }
        public string OneRosterBaseUrl { get; set; }
        public string OAuth2TokenUrl { get; set; }
        public string ClientId { get; set; }
    }
}
