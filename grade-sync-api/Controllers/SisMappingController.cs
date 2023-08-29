using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Identity.Web;

using GradeSyncApi.Helpers;
using GradeSyncApi.Services.Graph;
using GradeSyncApi.Services.Graph.JsonEntities;
using GradeSyncApi.Services.Storage;
using GradeSyncApi.Services.OneRoster;

namespace GradeSyncApi.Controllers
{
    [ApiController]
    [Authorize]
    public class SisMappingController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SisMappingController> _logger;
        private readonly ITableStorageService _storageService;
        private readonly IGraphService _graphService;
        private readonly IOneRosterService _oneRosterService;

        public SisMappingController(
            IConfiguration configuration,
            ITableStorageService storageService,
            IGraphService graphService,
            IOneRosterService oneRoster,
            ILogger<SisMappingController> logger)
        {
            _configuration = configuration;
            _storageService = storageService;
            _graphService = graphService;
            _oneRosterService = oneRoster;
            _logger = logger;
        }

        private static bool HasTeacherExternalId(EducationUser? eduTeacher)
        {
            if (eduTeacher is null) return false;
            if (eduTeacher?.Teacher?.ExternalId is not null && eduTeacher?.Teacher?.ExternalId != "") return true;
            return false;
        }

        private static bool HasStudentExternalId(EducationUser? eduStudent)
        {
            if (eduStudent is null) return false;
            if (eduStudent?.Student?.ExternalId is not null && eduStudent?.Student?.ExternalId != "") return true;
            return false;
        }

        private static bool HasClassExternalId(ClassEntity? teamsClass)
        {
            if (teamsClass is null) return false;
            if (teamsClass?.ExternalId is not null && teamsClass?.ExternalId != "") return true;
            return false;
        }

        private bool MatchTeachersByEmail()
        {
            var matchEmail = _configuration.GetValue<string>("MatchEmailOneRoster");
            if (matchEmail is not null && matchEmail == "true") return true;
            return false;
        }

        private bool IsMappingToolDisabled()
        {
            var toolDisabled = _configuration.GetValue<string>("MappingToolDisabled");
            if (toolDisabled is not null && toolDisabled == "true") return true;
            return false;
        }

        [HttpGet]
        [Route("api/graph-external-id-mapping-state/{classId}")]
        public async Task<IActionResult> GraphExternalIdState(string classId)
        {
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);
            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);

            var mappingDto = new GraphExternalIdMappingDto();
            if (IsMappingToolDisabled())
            {
                mappingDto.MappingToolDisabled = true;
                return Ok(mappingDto);
            }
            else mappingDto.MappingToolDisabled = false;

            var teamsClass = await _graphService.GetClass(classId);
            mappingDto.HasClassExternalId = HasClassExternalId(teamsClass);

            var eduTeacher = await _graphService.GetEducationUserTeacher();
            mappingDto.HasAccountExternalId = HasTeacherExternalId(eduTeacher);

            // check that all students in this class have an external id
            bool allStudentsHaveId = true;
            var msftStudents = await _graphService.GetStudentsByClass(classId);
            foreach (var student in msftStudents!)
            {
                if (student.Student?.ExternalId is null)
                {
                    allStudentsHaveId = false;
                    break;
                }
            }
            mappingDto.HasAllStudentExternalIds = allStudentsHaveId;

            return Ok(mappingDto);
        }

        [HttpGet]
        [Route("api/get-class-external-id/{classId}")]
        public async Task<IActionResult> GetTeamsClassExternalId(string classId)
        {
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);

            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);
            var teamsClass = await _graphService.GetClass(classId);

            if (teamsClass?.ExternalId is not null && teamsClass?.ExternalId != "") return Ok(teamsClass!.ExternalId);
            return BadRequest();
        }

        [HttpGet]
        [Route("api/get-teacher-external-id")]
        public async Task<IActionResult> GetTeacherExternalId()
        {
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);

            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);
            var eduTeacher = await _graphService.GetEducationUserTeacher();
            if (HasTeacherExternalId(eduTeacher))
            {
                return Ok(eduTeacher?.Teacher.ExternalId);
            }
            else return BadRequest();
        }

        [HttpGet]
        [Route("api/get-one-roster-teacher-matches/{connectionId}")]
        public async Task<IActionResult> GetOneRosterTeacherMatchByAdUser(string connectionId)
        {
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);
            var connection = await _storageService.GetOneRosterConnectionEntity(claims["tid"], connectionId);
            if (connection is null) return BadRequest();

            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);
            var eduTeacher = await _graphService.GetEducationUserTeacher();

            await _oneRosterService.InitApiConnection(connection);
            try
            {
                var teachers = await _oneRosterService
                    .GetTeachersMatchedByEmail(MatchTeachersByEmail() ? eduTeacher!.Email : null);
                return Ok(teachers);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e, "Error getting OneRoster teachers for mapping tool.");
                return BadRequest();
            }
        }

        [HttpGet]
        [Route("api/get-teams-and-oneroster-students/{classId}/{connectionId}/{inMemClassExternalId?}")]
        public async Task<IActionResult> GetTeamsAndOneRosterStudents(string classId, string connectionId, string? inMemClassExternalId = null)
        {
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);
            var connection = await _storageService.GetOneRosterConnectionEntity(claims["tid"], connectionId);
            if (connection is null) return BadRequest();

            await _oneRosterService.InitApiConnection(connection);
            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);
            var teamsClass = await _graphService.GetClass(classId);
            if (!HasClassExternalId(teamsClass) && inMemClassExternalId is null) return BadRequest();
            
            var authorized = await _graphService.CanAccessClass(classId);
            if (authorized)
            {
                try
                {
                    var msftStudents = await _graphService.GetStudentsByClass(classId);
                    var oneRosterStudents = await _oneRosterService
                        .GetStudentUsersByClass(inMemClassExternalId is null ? teamsClass!.ExternalId : inMemClassExternalId);
                    return Ok(new MappingStudentsDto { GraphStudents = msftStudents, OneRosterStudents = oneRosterStudents });
                }
                catch (Exception e)
                {
                    _logger.Log(LogLevel.Error, e, "Error getting either Graph or OneRoster students for mapping tool.");
                    return BadRequest();
                }
            }
            else return Unauthorized();
        }

        [HttpGet]
        [Route("api/get-one-roster-classes/{connectionId}/{inMemTeacherExternalId?}")]
        public async Task<IActionResult> GetOneRosterTeacherClasses(string connectionId, string? inMemTeacherExternalId = null)
        {
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);
            var connection = await _storageService.GetOneRosterConnectionEntity(claims["tid"], connectionId);
            if (connection is null) return BadRequest();

            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);
            var isAdmin = await _graphService.HasAdminRole();
            var eduTeacher = await _graphService.GetEducationUserTeacher();
            if (!isAdmin && !HasTeacherExternalId(eduTeacher) && inMemTeacherExternalId is null) return BadRequest();

            await _oneRosterService.InitApiConnection(connection);
            try
            {
                var classes = await _oneRosterService
                    .GetClasses(inMemTeacherExternalId is null ? eduTeacher?.Teacher?.ExternalId : inMemTeacherExternalId, isAdmin);
                return Ok(classes);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e, "Error getting OneRoster classes for mapping tool.");
                return BadRequest();
            }
        }

        [HttpGet]
        [Route("api/patch-teacher-external-id/{externalId}/{connectionId}")]
        public async Task<IActionResult> PatchTeacherExternalId(string externalId, string connectionId)
        {
            if (IsMappingToolDisabled()) return Unauthorized();
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);

            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);
            var isAdmin = await _graphService.HasAdminRole();
            var eduTeacher = await _graphService.GetEducationUserTeacher();

            // teachers can only change their externalId if they don't already have one
            // if teacher already has an externalId, only an admin can change it to something else
            bool teacherCanChange = !HasTeacherExternalId(eduTeacher);
            if (teacherCanChange || isAdmin)
            {
                try
                {
                    if (!isAdmin && MatchTeachersByEmail())
                    {
                        // in this case we need to verify again that the teacher's AD email matches the OneRoster account they are attempting to map to
                        var connection = await _storageService.GetOneRosterConnectionEntity(claims["tid"], connectionId);
                        if (connection is null) return BadRequest();

                        await _oneRosterService.InitApiConnection(connection);
                        var teachers = await _oneRosterService.GetTeachersMatchedByEmail(eduTeacher!.Email);

                        bool accountWithIdExists = false;
                        foreach (var teacher in teachers)
                        {
                            if (teacher.Id == externalId)
                            {
                                accountWithIdExists = true;
                                break;
                            }
                        }
                        if (!accountWithIdExists) return BadRequest();
                    }

                    // need to change token to application permissions
                    await _graphService.ExchangeGraphTokenClientCredentials(claims["tid"]);
                    await _graphService.PatchTeacherExternalId(eduTeacher!.UserId, externalId);
                }
                catch (Exception e)
                {
                    _logger.Log(LogLevel.Error, e, "Error patching teacher external ID for mapping tool.");
                    return BadRequest();
                }
            }
            else return Unauthorized();
            return Ok();
        }

        [HttpGet]
        [Route("api/patch-class-external-id/{classId}/{externalId}/{connectionId}")]
        public async Task<IActionResult> PatchClassExternalId(string classId, string externalId, string connectionId)
        {
            if (IsMappingToolDisabled()) return Unauthorized();
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);

            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);
            var isAdmin = await _graphService.HasAdminRole();
            var authorized = await _graphService.CanAccessClass(classId);
            var teamsClass = await _graphService.GetClass(classId);

            // ensure the externalId is a OneRoster class the teacher actually teaches, unless they are an admin
            if (!isAdmin)
            {
                var connection = await _storageService.GetOneRosterConnectionEntity(claims["tid"], connectionId);
                if (connection is null) return BadRequest();
                
                var eduTeacher = await _graphService.GetEducationUserTeacher();
                if (!HasTeacherExternalId(eduTeacher)) return BadRequest();

                await _oneRosterService.InitApiConnection(connection);
                bool teachesOneRosterClass = false;
                var classes = await _oneRosterService.GetClasses(eduTeacher?.Teacher?.ExternalId, false);
                foreach (var oneRosterClass in classes)
                {
                    if (oneRosterClass.Id == externalId)
                    {
                        teachesOneRosterClass = true;
                        break;
                    }
                }

                if (!teachesOneRosterClass) return Unauthorized();
            }

            // teachers can only change the Teams class externalId if they don't already have one
            // if Teams class already has an externalId, only an admin can change it to something else
            if ((!HasClassExternalId(teamsClass) && authorized) || isAdmin)
            {
                try
                {
                    // need to change token to application permissions
                    await _graphService.ExchangeGraphTokenClientCredentials(claims["tid"]);
                    await _graphService.PatchClassExternalId(classId, externalId);
                }
                catch (Exception) { return BadRequest(); }
            }
            else return Unauthorized();
            return Ok();
        }

        [HttpGet]
        [Route("api/patch-student-external-id/{classId}/{studentId}/{externalId}/{connectionId}")]
        public async Task<IActionResult> PatchStudentExternalId(string classId, string studentId, string externalId, string connectionId)
        {
            if (IsMappingToolDisabled()) return Unauthorized();
            var bearerToken = await HttpContext!.GetTokenAsync("access_token");
            var claims = ClaimsHelper.ClaimsToDict(HttpContext!.User.Claims);

            await _graphService.ExchangeGraphToken(bearerToken!, claims["tid"]);
            var isAdmin = await _graphService.HasAdminRole();
            var authorized = await _graphService.CanAccessClass(classId);
            var msftStudents = await _graphService.GetStudentsByClass(classId);
            var studentToPatch = msftStudents!.Find(student => student.UserId == studentId);

            // this check ensures the teacher has the rights to the class and that the student is
            // actually in this specific class
            if (!authorized || studentToPatch is null) return Unauthorized();

            // make sure the student externalID is in the oneroster class
            if (!isAdmin)
            {
                var connection = await _storageService.GetOneRosterConnectionEntity(claims["tid"], connectionId);
                if (connection is null) return BadRequest();

                var teamsClass = await _graphService.GetClass(classId);
                if (!HasClassExternalId(teamsClass)) return BadRequest();

                await _oneRosterService.InitApiConnection(connection);
                bool studentIsEnrolled = false;
                var students = await _oneRosterService.GetStudentUsersByClass(teamsClass!.ExternalId);
                foreach (var student in students)
                {
                    if (student.Id == externalId)
                    {
                        studentIsEnrolled = true;
                        break;
                    }
                }

                if (!studentIsEnrolled) return BadRequest();
            }

            // teachers can only change a student's externalId if they don't already have one
            // if student already has an externalId, only an admin can change it to something else
            bool teacherCanChange = !HasStudentExternalId(studentToPatch);
            if (teacherCanChange || isAdmin)
            {
                try
                {
                    // need to change token to application permissions
                    await _graphService.ExchangeGraphTokenClientCredentials(claims["tid"]);
                    await _graphService.PatchStudentExternalId(studentId, externalId);
                }
                catch (Exception) { return BadRequest(); }
            }
            else return BadRequest();
            return Ok();
        }
    }
}
