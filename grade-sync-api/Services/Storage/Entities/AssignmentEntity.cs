// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Runtime.Serialization;
using GradeSyncApi.Services.OneRoster;
using Newtonsoft.Json;

namespace GradeSyncApi.Services.Storage
{
    public enum GradeSyncStatus
    {
        NotSynced,
        InProgress,
        Synced,
        Failed,
        Cancelled
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class AssignmentEntity : BaseEntity
    {
        public AssignmentEntity() {}

        [JsonIgnore]
        [IgnoreDataMember]
        private static readonly SemaphoreSlim _catPersistLock = new SemaphoreSlim(1, 1);

        public static string TableName = "Assignments";
        // Status and last sync timestamp should be updated from grade-sync-worker to reflect progress
        public GradeSyncStatus SyncStatus { get; set; }
        public string CurrentSyncJobId { get; set; }
        public string LastSyncTimestamp { get; set; } 

        [JsonProperty("id")]
        public string AssignmentId { get; set; }

        [JsonProperty("classId")]
        public string ClassId { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("assignedDateTime")]
        public string AssignedTimestamp { get; set; }

        [JsonProperty("dueDateTime")]
        public string DueTimestamp { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        public int? MaxPoints { get; set; }

        [JsonProperty("graphGradingCategory")]
        public string? GraphGradingCategoryName { get; set; }

        public string? Desc { get; set; }

        public string? GradeSyncErrorMessage { get; set; }

        public string? StringifiedCategoryDict { get; set; }

        public Dictionary<string, CategoryMapping>? DeserializeCategoryDict()
        {
            if (StringifiedCategoryDict is null || StringifiedCategoryDict == "") return null;
            return JsonConvert.DeserializeObject<Dictionary<string, CategoryMapping>>(StringifiedCategoryDict);
        }

        public void AddCategoryMapping(string oneRosterConnectionId, string categoryId)
        {
            var categoryDict = DeserializeCategoryDict();
            if (categoryDict is null) categoryDict = new Dictionary<string, CategoryMapping>();
            // either add or overwrite categoryId for this connectionId
            if (categoryDict.TryGetValue(oneRosterConnectionId, out CategoryMapping value))
            {
                // can only change existing category if the lineItem has not been synced yet
                if (!value.LineItemSynced)
                {
                    value.CatId = categoryId;
                    categoryDict[oneRosterConnectionId] = value;
                }
            } else
            {
                categoryDict[oneRosterConnectionId] = new CategoryMapping(categoryId, false);
            }
            
            StringifiedCategoryDict = JsonConvert.SerializeObject(categoryDict);
        }

        public async Task PersistCreatedCategory(string oneRosterConnectionId, LineItem? lineItem)
        {
            string? createdCatId = lineItem?.Category?.Id;
            await _catPersistLock.WaitAsync();

            try
            {
                var categoryDict = DeserializeCategoryDict();

                if (createdCatId is not null && categoryDict is null)
                {
                    categoryDict = new Dictionary<string, CategoryMapping>();
                    categoryDict[oneRosterConnectionId] = new CategoryMapping(createdCatId, true);
                }
                else if (categoryDict is not null)
                {
                    if (createdCatId is null) createdCatId = "none";

                    if (categoryDict.TryGetValue(oneRosterConnectionId, out CategoryMapping value))
                    {
                        value.LineItemSynced = true;
                        // in most cases this will match the catId the user selected and be a no-op, but sometimes
                        // it may overwrite if the lineItem gets created with a default catId that is different than the one the user specified
                        value.CatId = createdCatId;
                        categoryDict[oneRosterConnectionId] = value;
                    }
                    else
                    {
                        categoryDict[oneRosterConnectionId] = new CategoryMapping(createdCatId, true);
                    }
                }

                if (categoryDict is not null)
                {
                    StringifiedCategoryDict = JsonConvert.SerializeObject(categoryDict);
                }
            } finally
            {
                _catPersistLock.Release();
            }
        }

        [JsonIgnore]
        [IgnoreDataMember]
        public List<string>? SubmissionSyncErrors { get; set; }

        // these fields are used in grade-sync-worker to track if a lineItem or lineItemResult failed during creation in OneRoster.
        // since we only track it in-memory and don't persist it, it allows us to know if it was during the current job
        [JsonIgnore]
        [IgnoreDataMember]
        public bool LineItemFailedInMemory { get; set; }

        [JsonIgnore]
        [IgnoreDataMember]
        public bool LineItemResultFailedInMemory { get; set; }

        [JsonIgnore]
        [IgnoreDataMember]
        public bool ForceSync { get; set; }

        [JsonProperty("grading")]
        private Grading Grading
        {
            set
            {
                if (value is null)
                {
                    MaxPoints = null;
                }
                else
                {
                    MaxPoints = value.MaxPoints;
                }
            }
        }

        [JsonProperty("gradingCategory")]
        private GradingCategory GradingCategory
        {
            set
            {
                if (value is not null)
                {
                    GraphGradingCategoryName = value.DisplayName;
                }
            }
        }

        [JsonProperty("instructions")]
        private Instructions Instructions
        {
            set
            {
                if (value is null)
                {
                    Desc = null;
                }
                else
                {
                    Desc = value.Content;
                    /*
                    if (value.Type == "Text")
                    {
                        Desc = value.Content;
                    }
                    */
                }
            }
        }
    }

    public class Grading
    {
        [JsonProperty("maxPoints")]
        public int MaxPoints { get; set; }
    }

    public class GradingCategory
    {
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }
    }

    public class Instructions
    {
        [JsonProperty("contentType")]
        public string Type { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }
    }

    public class CategoryMapping
    {
        public CategoryMapping(string catId, bool synced)
        {
            CatId = catId;
            LineItemSynced = synced;
        }

        [JsonProperty("catId")]
        public string CatId { get; set; }

        [JsonProperty("lineItemSynced")]
        public bool LineItemSynced { get; set; }
    }
}
