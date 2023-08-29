using System;
using GradeSyncApi.Services.OneRoster;

namespace GradeSyncApi.Services.Graph.JsonEntities
{
    public class GraphExternalIdMappingDto
    {
        public GraphExternalIdMappingDto() { }

        public bool HasAccountExternalId { get; set; }

        public bool HasClassExternalId { get; set; }

        public bool HasAllStudentExternalIds { get; set; }

        public bool MappingToolDisabled { get; set; }
    }

    public class MappingStudentsDto
    {
        public MappingStudentsDto() { }

        public List<EducationUser>? GraphStudents { get; set; }

        public List<OneRosterUser>? OneRosterStudents { get; set; }

    }
}

