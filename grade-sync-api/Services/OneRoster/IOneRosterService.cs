using System;
using GradeSyncApi.Services.Storage;

namespace GradeSyncApi.Services.OneRoster
{
    public interface IOneRosterService
    {
        Task InitApiConnection(OneRosterConnectionEntity entity);
        Task GetCurrentGradingPeriod();
        Task<LineItem?> CreateLineItem(string sourcedId, AssignmentEntity assignmentEntity, string classExternalId, string oneRosterConnectionId);
        Task<bool> OneRosterResourceExists(string sourcedId, OneRosterResourceType resourceType);
        Task<Result?> CreateLineItemResult(SubmissionEntity submission, string studentSisId, string assignmentId, string? subClassExternalId = null);
        Task<List<Category>> GetActiveCategories();
        Task ValidateClassGroups();
        Task<ClassGroup> GetClassGroup(string classGroupSourcedId);
        Task<List<Enrollment>> GetEnrollmentsByClass(string classSourcedId);
        Task<List<OneRosterUser>> GetTeachersMatchedByEmail(string? adUserEmail = null);
        Task<List<OneRosterClass>> GetClasses(string? teacherSourcedId, bool isAdmin);
        Task<List<OneRosterUser>> GetStudentUsersByClass(string classSourcedId);
    }
}

