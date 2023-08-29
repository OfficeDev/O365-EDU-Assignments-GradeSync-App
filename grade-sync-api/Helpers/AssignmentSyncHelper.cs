using System;
using Azure.Data.Tables;
using GradeSyncApi.Services.Storage;

namespace GradeSyncApi.Helpers
{
    public class AssignmentSyncHelper
    {
        private List<AssignmentEntity> _graphAssignments;
        private List<AssignmentEntity>? _storedAssignments;
        private Dictionary<string, AssignmentEntity> _graphDict;
        private Dictionary<string, AssignmentEntity> _storedDict;

        public List<TableTransactionAction> AssignmentsToUpsert { get; set; }
        public List<TableTransactionAction> AssignmentsToDelete { get; set; }

        public AssignmentSyncHelper(List<AssignmentEntity> graphAssignments, List<AssignmentEntity>? storedAssignments)
        {
            _graphAssignments = graphAssignments;
            _storedAssignments = storedAssignments;

            _graphDict = new Dictionary<string, AssignmentEntity>();
            _assignmentsToDict(_graphDict, _graphAssignments);

            _storedDict = new Dictionary<string, AssignmentEntity>();
            if (_storedAssignments is not null)
            {
                _assignmentsToDict(_storedDict, _storedAssignments);
            }

            AssignmentsToUpsert = new List<TableTransactionAction>();
            AssignmentsToDelete = new List<TableTransactionAction>();
        }

        private void _assignmentsToDict(Dictionary<string, AssignmentEntity> dict, List<AssignmentEntity> assignments)
        {
            foreach (var assignment in assignments)
            {
                dict.Add(assignment.AssignmentId, assignment);
            }
        }

        public void SyncState()
        {
            foreach (var graphAssignment in _graphAssignments)
            {
                graphAssignment.PartitionKey = graphAssignment.ClassId;
                graphAssignment.RowKey = graphAssignment.AssignmentId;

                if (_storedDict.TryGetValue(graphAssignment.AssignmentId, out AssignmentEntity storedAssignment))
                {
                    graphAssignment.SyncStatus = storedAssignment.SyncStatus;
                    graphAssignment.LastSyncTimestamp = storedAssignment.LastSyncTimestamp;
                    graphAssignment.GradeSyncErrorMessage = storedAssignment.GradeSyncErrorMessage;
                    graphAssignment.CurrentSyncJobId = storedAssignment.CurrentSyncJobId;
                    graphAssignment.StringifiedCategoryDict = storedAssignment.StringifiedCategoryDict;
                }
                
                var action = new TableTransactionAction(TableTransactionActionType.UpsertMerge, graphAssignment);
                AssignmentsToUpsert.Add(action);
            }

            if (_storedAssignments is not null)
            {
                foreach (var storedAssignment in _storedAssignments)
                {
                    if (!_graphDict.TryGetValue(storedAssignment.AssignmentId, out AssignmentEntity value))
                    {
                        // this means the assignment has been deleted in Teams but we still have it stored
                        var action = new TableTransactionAction(TableTransactionActionType.Delete, storedAssignment);
                        AssignmentsToDelete.Add(action);
                    }
                }
            }
        }

        public List<AssignmentEntity> GetModifiedGraphAssignments()
        {
            return _graphAssignments;
        }
    }
}

