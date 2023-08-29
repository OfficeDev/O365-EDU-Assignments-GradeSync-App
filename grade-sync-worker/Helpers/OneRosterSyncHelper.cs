using Azure.Data.Tables;
using System;
using Microsoft.Extensions.Configuration;
using GradeSyncApi.Services.Storage;
using GradeSyncApi.Services.OneRoster;

namespace grade_sync_worker.Helpers
{
    public class OneRosterSyncHelper
    {
        public OneRosterSyncHelper() {}

        public static async Task UpsertAllLineItems(IOneRosterService oneRosterService, GradeSyncJobEntity jobEntity, IConfiguration configuration)
        {
            var taskList = new List<Task>();
            if (jobEntity.AssignmentEntities is not null)
            {
                foreach (var assignment in jobEntity.AssignmentEntities)
                {
                    assignment.LineItemFailedInMemory = false;
                    assignment.LineItemResultFailedInMemory = false;
                    assignment.CurrentSyncJobId = jobEntity.RowKey!;

                    if (jobEntity.ConnectionEntity.IsGroupEnabled)
                    {
                        if (jobEntity.ListSubclassExternalIds is not null)
                        {
                            foreach (var subclassExternalId in jobEntity.ListSubclassExternalIds)
                            {
                                var task = UpsertLineItem(
                                    assignment,
                                    subclassExternalId,
                                    oneRosterService,
                                    jobEntity.OneRosterConnectionId,
                                    jobEntity.ConnectionEntity.IsGroupEnabled
                                );
                                taskList.Add(task);
                            }
                        }
                    }
                    else
                    {
                        var task = UpsertLineItem(
                            assignment,
                            jobEntity.ClassExternalId,
                            oneRosterService,
                            jobEntity.OneRosterConnectionId,
                            jobEntity.ConnectionEntity.IsGroupEnabled
                        );
                        taskList.Add(task);
                    }
                }
            }

            await Task.WhenAll(taskList);
        }

        public static async Task UpsertAllSubmissions(IOneRosterService oneRosterService, GradeSyncJobEntity jobEntity)
        {
            var taskList = new List<Task>();
            foreach (var assignment in jobEntity.AssignmentEntities!)
            {
                // if the lineItem sync failed during this job then we don't want to attempt to sync any lineItemResults
                if (assignment.LineItemFailedInMemory) continue;
                
                assignment.SubmissionSyncErrors = new List<string>();

                var submissions = jobEntity.AssignmentsWithSubmissions[assignment.RowKey!];
                if (submissions is not null)
                {
                    foreach (var submission in submissions)
                    {
                        if (submission.SubmissionStatus == SubmissionStatus.Returned)
                        {
                            string? studentSisId;
                            if (jobEntity.StudentSisIdDict.TryGetValue(submission.SubmittedByUser.User.Id, out string value))
                            {
                                studentSisId = value == "" ? null : value;
                            }
                            else continue; // solves an edge case where a teacher can manually create a submission for student that isn't actually licensed in AD

                            if (!jobEntity.ConnectionEntity.IsGroupEnabled || (jobEntity.ConnectionEntity.IsGroupEnabled && studentSisId is null))
                            {
                                var task = UpsertSubmission(
                                    assignment,
                                    oneRosterService,
                                    submission,
                                    studentSisId
                                );
                                taskList.Add(task);
                            }
                            else
                            {
                                // if connection is group enabled, we just need to figure out which subClass(s) the student is enrolled in, and pass that
                                // as a param for subClassExternalId
                                if (jobEntity.ClassGroupEnrollmentsUserMap!.TryGetValue(studentSisId!, out List<string> idList))
                                {
                                    // this should generally only be one subClass in the group that the student is enrolled in,
                                    // but just in case they are enrolled in multiple sections of the same class we need to account for it
                                    foreach (var classSourcedId in idList)
                                    {
                                        var task = UpsertSubmission(
                                            assignment,
                                            oneRosterService,
                                            submission,
                                            studentSisId,
                                            classSourcedId
                                        );

                                        taskList.Add(task);
                                    }
                                }
                                else
                                {
                                    // this could also be the case if they DO have a sisId, but it was set incorrectly and doesn't correspond to any of the sisId's fetched from enrollments
                                    // via classGroups endpoint
                                    var task = UpsertSubmission(
                                        assignment,
                                        oneRosterService,
                                        submission,
                                        null
                                    );
                                    taskList.Add(task);
                                }
                            }
                        }
                    }
                }
            }

            await Task.WhenAll(taskList);
        }

        private static async Task UpsertLineItem(
            AssignmentEntity assignmentEntity,
            string classExternalId,
            IOneRosterService oneRosterService,
            string oneRosterConnectionId,
            bool isGroupEnabledConnection)
        {
            try
            {
                if (assignmentEntity.Status != "assigned")
                {
                    throw new ApplicationException("Cannot sync Graph assignment that doesn't have status='assigned'");
                }

                var sourcedId = "";
                if (isGroupEnabledConnection)
                {
                    // if the OneRoster connection uses /classGroups, we need to make a composite assignment key
                    // because the same lineItem needs to be created in multiple classes, and the id must be different
                    sourcedId = $"{assignmentEntity.RowKey!}-{classExternalId}";
                }
                else sourcedId = assignmentEntity.RowKey!;

                var exists = assignmentEntity.ForceSync ? false : await oneRosterService.OneRosterResourceExists(sourcedId, OneRosterResourceType.LineItem);
                if (!exists)
                {
                    var createdLineItem = await oneRosterService.CreateLineItem(sourcedId, assignmentEntity, classExternalId, oneRosterConnectionId);
                    await assignmentEntity.PersistCreatedCategory(oneRosterConnectionId, createdLineItem);
                } 
            }
            catch (Exception e)
            {
                assignmentEntity.SyncStatus = GradeSyncStatus.Failed;
                assignmentEntity.GradeSyncErrorMessage = e.Message;
                assignmentEntity.LineItemFailedInMemory = true;
            }
        }

        private static async Task UpsertSubmission(
            AssignmentEntity assignmentEntity,
            IOneRosterService oneRosterService,
            SubmissionEntity submission,
            string? studentSisId,
            string? subClassExternalId = null
        )
        {
            try
            {
                if (studentSisId is null) throw new ApplicationException(
                    $"Cannot create lineItem result because Active directory student {submission.SubmittedByUser.User.Id} has a missing or incorrect SIS/Exernal ID. Any other valid student submissions were synced.");

                var exists = assignmentEntity.ForceSync ? false : await oneRosterService.OneRosterResourceExists(submission.SubmissionId, OneRosterResourceType.LineItemResult);
                if (!exists)
                {
                    await oneRosterService.CreateLineItemResult(submission, studentSisId, assignmentEntity.RowKey!, subClassExternalId);
                }
            }
            catch (Exception e)
            {
                assignmentEntity.SyncStatus = GradeSyncStatus.Failed;
                assignmentEntity.SubmissionSyncErrors!.Add(e.Message);
                assignmentEntity.LineItemResultFailedInMemory = true;
            }
        }

        public static async Task<Dictionary<string, List<string>>?> GetClassGroupEnrollmentsUserMap(GradeSyncJobEntity jobEntity, IOneRosterService oneRosterService)
        {
            var classGroup = await oneRosterService.GetClassGroup(jobEntity.ClassExternalId);
            if (classGroup is not null)
            {
                var enrollmentTasks = new List<Task<List<Enrollment>>>();
                jobEntity.ListSubclassExternalIds = new List<string>();

                foreach (var childClass in classGroup.Classes)
                {
                    var task = oneRosterService.GetEnrollmentsByClass(childClass.Id);
                    enrollmentTasks.Add(task);
                    jobEntity.ListSubclassExternalIds.Add(childClass.Id);
                }

                await Task.WhenAll(enrollmentTasks);
                return EnrollmentsToUserIdMap(enrollmentTasks);
            }
            else return null;
        }

        private static Dictionary<string, List<string>> EnrollmentsToUserIdMap(List<Task<List<Enrollment>>> enrollmentTasks)
        {
            var userIdMap = new Dictionary<string, List<string>>();
            foreach (var task in enrollmentTasks)
            {
                var enrollments = task.Result;
                foreach (var enrollment in enrollments)
                {
                    if (userIdMap.ContainsKey(enrollment.User.Id))
                    {
                        userIdMap[enrollment.User.Id].Add(enrollment.Class.Id);
                    }
                    else
                    {
                        userIdMap[enrollment.User.Id] = new List<string>
                        {
                            enrollment.Class.Id
                        };
                    }
                }
            }

            return userIdMap;
        }

        public static async Task AssignmentStatusUpdateAndResync(GradeSyncJobEntity jobEntity, ITableStorageService tableStorageService)
        {
            var timeNow = DateTime.UtcNow;

            var upsertList = new List<TableTransactionAction>();
            foreach (var assignment in jobEntity.AssignmentEntities!)
            {
                assignment.LastSyncTimestamp = timeNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                if (assignment.LineItemFailedInMemory || assignment.LineItemResultFailedInMemory)
                {
                    if (assignment.SubmissionSyncErrors is not null)
                    {
                        if (assignment.SubmissionSyncErrors.Count > 0)
                        {
                            assignment.GradeSyncErrorMessage = string.Join("\n", assignment.SubmissionSyncErrors);
                        }
                    }
                }
                else
                {
                    assignment.SyncStatus = GradeSyncStatus.Synced;
                }

                upsertList.Add(new TableTransactionAction(TableTransactionActionType.UpsertMerge, assignment));
            }

            await tableStorageService.BatchTransactAssignmentsAsync(upsertList);
        }

        public static async Task MarkJobComplete(GradeSyncJobEntity jobEntity, ITableStorageService tableStorageService)
        {
            jobEntity.JobStatus = GradeSyncJobStatus.Finished;
            await tableStorageService.UpsertGradeSyncJobEntityAsync(jobEntity);
        }

        public static async Task MarkJobCompleteAndAssignmentsFailed(GradeSyncJobEntity jobEntity, ITableStorageService tableStorageService)
        {
            // if job was cancelled we don't need to do anything
            if (jobEntity.JobStatus != GradeSyncJobStatus.Cancelled)
            {
                var timeNow = DateTime.UtcNow;
                var upsertList = new List<TableTransactionAction>();

                foreach (var assignment in jobEntity.AssignmentEntities!)
                {
                    assignment.LastSyncTimestamp = timeNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                    assignment.GradeSyncErrorMessage = "Sync job failed. Try and run your sync at a later time, or contact an admin if the issue continues.";
                    assignment.SyncStatus = GradeSyncStatus.Failed;
                    upsertList.Add(new TableTransactionAction(TableTransactionActionType.UpsertMerge, assignment));
                }

                await tableStorageService.BatchTransactAssignmentsAsync(upsertList);
                jobEntity.JobStatus = GradeSyncJobStatus.Finished;
                await tableStorageService.UpsertGradeSyncJobEntityAsync(jobEntity);
            }
        }
    }
}
