using System;
using GradeSyncApi.Services.Storage;
using GradeSyncApi.Services.Graph.JsonEntities;

namespace GradeSyncApi.Services.Graph
{
    public interface IGraphService
    {
        Task ExchangeGraphToken(string idToken, string tenantId);
        Task ExchangeGraphTokenClientCredentials(string tenantId, bool azureFuncEnv = false);

        Task<List<EducationUser>?> GetStudentsByClass(string classId);
        Task<List<AssignmentEntity>?> GetAssignmentsByClass(string classId);
        Task<bool> CanAccessClass(string classId);
        Task<bool> HasAdminRole();
        Task<ClassEntity?> GetClass(string classId);
        Task<Dictionary<string, List<SubmissionEntity>>> GetAllAssignmentsWithSubmissions(string classId, List<string> assignmentIdList);
        Task<Dictionary<string, string>> GetStudentSisIdDict(string classId);
        Task<bool> IsStudentPrimaryRole();
        Task PatchClassExternalId(string classId, string externalId);
        Task PatchStudentExternalId(string userId, string externalId);
        Task PatchTeacherExternalId(string userId, string externalId);
        Task<EducationUser?> GetEducationUserTeacher();
    }
}

