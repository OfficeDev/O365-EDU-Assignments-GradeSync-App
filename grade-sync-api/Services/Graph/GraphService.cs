using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Identity.Client;
using Newtonsoft.Json;

using GradeSyncApi.Services.Graph.JsonEntities;
using GradeSyncApi.Services.Storage;

namespace GradeSyncApi.Services.Graph
{
    public class GraphService : IGraphService
    {
        private readonly ILogger<GraphService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IDirectoryRoleService _roleService;

        private HttpClient? _httpClient;
        private string? _graphAccessToken;
        private const string _graphEduBaseUrl = "https://graph.microsoft.com/v1.0/education";
        private const string _graphEduBetaUrl = "https://graph.microsoft.com/beta/education";
        private const string _graphBaseUrl = "https://graph.microsoft.com/v1.0";

        public GraphService(IConfiguration configuration, ILogger<GraphService> logger, IDirectoryRoleService roleService)
        {
            _configuration = configuration;
            _logger = logger;
            _roleService = roleService;
        }

        public async Task ExchangeGraphToken(string idToken, string tenantId)
        {
            var adConfigs = _configuration.GetSection("AzureAd");

            var app = ConfidentialClientApplicationBuilder
                .Create(adConfigs["ClientId"])
                .WithClientSecret(adConfigs["ClientSecret"])
                .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                .Build();

            try
            {
                var assertion = new UserAssertion(idToken);
                var scopes = new List<string>
                {
                    "https://graph.microsoft.com/User.Read",
                    "https://graph.microsoft.com/EduAdministration.Read",
                    "https://graph.microsoft.com/EduAssignments.Read",
                    "https://graph.microsoft.com/EduRoster.Read",
                    "https://graph.microsoft.com/EduRoster.ReadBasic"
                };

                var res = await app.AcquireTokenOnBehalfOf(scopes, assertion).ExecuteAsync();
                _graphAccessToken = res.AccessToken.ToString();

                _httpClient = new HttpClient();
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _graphAccessToken);
            } catch (Exception e)
            {
                _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, e, $"ID token: {idToken}");
                _graphAccessToken = null;
            }
        }

        public async Task ExchangeGraphTokenClientCredentials(string tenantId, bool azureFuncEnv = false)
        {
            // required for getting a token with Application scope permissions e.g. not on behalf of a user
            var clientId = "";
            var clientSecret = "";
            if (azureFuncEnv)
            {
                clientId = _configuration["ClientId"];
                clientSecret = _configuration["ClientSecret"];
            }
            else
            {
                var adConfigs = _configuration.GetSection("AzureAd");
                clientId = adConfigs["ClientId"];
                clientSecret = adConfigs["ClientSecret"];
            }
            
            var app = ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithClientSecret(clientSecret)
                .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                .Build();

            try
            {
                var scopes = new List<string>();
                scopes.Add("https://graph.microsoft.com/.default");
                var res = await app.AcquireTokenForClient(scopes).ExecuteAsync();
                _graphAccessToken = res.AccessToken.ToString();

                _httpClient = new HttpClient();
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _graphAccessToken);
            }
            catch (Exception e)
            {
                _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, e, "Client credentials graph token grant failed.");
                _graphAccessToken = null;
            }
        }

        private static async Task<T?> GraphGetRequest<T>(HttpClient client, string url)
        {
            var res = await client!.GetAsync(url);
            res.EnsureSuccessStatusCode();
            var content = await res.Content.ReadAsStringAsync();
            var wrapper = JsonConvert.DeserializeObject<T>(content);
            return wrapper;
        }

        private static async Task GraphPatchRequest(HttpClient client, string url, object reqContent)
        {
            var serialized = JsonConvert
                .SerializeObject(reqContent, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            var requestContent = new StringContent(serialized, Encoding.UTF8, "application/json");
            var req = new HttpRequestMessage(HttpMethod.Patch, url)
            {
                Content = requestContent
            };

            var res = await client!.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();
            try
            {
                res.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException)
            {
                throw new ApplicationException(content);
            }
        }

        // API methods

        public async Task<bool> HasAdminRole()
        {
            var directoryWrapper = await GraphGetRequest<DirectoryWrapper>(_httpClient!, $"{_graphBaseUrl}/me/memberOf");
            var roleIds = _roleService.GetAdminRoleIds();

            foreach (var role in directoryWrapper!.Roles)
            {
                if (role.RoleTemplateId is not null)
                {
                    if (roleIds.Contains(role.RoleTemplateId))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public async Task<bool> IsStudentPrimaryRole()
        {
            try
            {
                var eduUser = await GraphGetRequest<EducationUser>(_httpClient!, $"{_graphEduBaseUrl}/me");
                if (eduUser is null) return false;

                if (eduUser.Role is not null)
                {
                    if (eduUser.Role == "student") return true;
                }
                return false;
            } catch (Exception e)
            {
                _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, e, "MS Graph error getting student primary role.");
                return true;
            }
        }

        public async Task<EducationUser?> GetEducationUserTeacher()
        {
            try
            {
                var eduUser = await GraphGetRequest<EducationUser>(_httpClient!, $"{_graphEduBaseUrl}/me");
                if (eduUser is null) return null;
                return eduUser;
            }
            catch (Exception e)
            {
                _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, e, "MS Graph error getting teacher external ID.");
                return null;
            }
        }

        public async Task<bool> CanAccessClass(string classId)
        {
            try
            {
                var teamsClasses = await GraphGetRequest<TeamsClassWrapper>(_httpClient!, $"{_graphEduBaseUrl}/me/taughtClasses");
                var idSet = _getTeamsClassIdSet(teamsClasses!);

                if (idSet.Contains(classId))
                {
                    return true;
                }
                else
                {
                    // if they aren't a teacher of the class, check to see if they are a tenant admin since admins should
                    // be able to access the class as a teacher
                    var admin = await HasAdminRole();
                    if (admin) return true;
                    return false;
                }
            }
            catch (Exception e)
            {
                _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, e, "MS Graph error checking class permissions.");
                return false;
            }
        }

        public async Task<ClassEntity?> GetClass(string classId)
        {
            try
            {
                var fields = "$select=id,displayName,externalId";
                var teamsClass = await GraphGetRequest<ClassEntity>(_httpClient!, $"{_graphEduBaseUrl}/classes/{classId}?{fields}");
                return teamsClass;
            } catch (Exception e)
            {
                _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, e, "MS Graph error fetching EDU class.");
                return null;
            }
        }

        public async Task PatchClassExternalId(string classId, string externalId)
        {
            try
            {
                var url = $"{_graphEduBaseUrl}/classes/{classId}";
                await GraphPatchRequest(_httpClient!, url, new ClassEntity(externalId));
            } catch (Exception e)
            {
                _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, e, "Error changing Graph EDU class externalId property.");
                throw new Exception(); // rethrow generic ex so we know in controller if the call failed
            }
        }

        public async Task PatchStudentExternalId(string userId, string externalId)
        {
            try
            {
                var url = $"{_graphEduBaseUrl}/users/{userId}";
                await GraphPatchRequest(_httpClient!, url, new EducationUser(externalId, false));
            }
            catch (Exception e)
            {
                _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, e, "Error changing Graph EDU student externalId property.");
                throw new Exception(); // rethrow generic ex so we know in controller if the call failed
            }
        }

        public async Task PatchTeacherExternalId(string userId, string externalId)
        {
            try
            {
                var url = $"{_graphEduBaseUrl}/users/{userId}";
                await GraphPatchRequest(_httpClient!, url, new EducationUser(externalId, true));
            }
            catch (Exception e)
            {
                _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, e, "Error changing Graph EDU teacher externalId property.");
                throw new Exception(); // rethrow generic ex so we know in controller if the call failed
            }
        }

        private static HashSet<string> _getTeamsClassIdSet(TeamsClassWrapper wrapper)
        {
            var idSet = new HashSet<string>();
            foreach (var teamsClass in wrapper.TeamsClasses)
            {
                idSet.Add(teamsClass.ClassId);
            }
            return idSet;
        }

        public async Task<List<EducationUser>?> GetStudentsByClass(string classId)
        {
            // we need to get both teachers and members for the class, and figure out who the students are based on the diff
            // in some cases you CAN use the primaryRole to know if they are a teacher or a student, but this value is not
            // necesarily populated depending on how your graph EDU users were set up, so this is a more dependable way to
            // distinguish who are students

            try
            {
                var studentWrapper = await GraphGetRequest<EducationUserWrapper>(_httpClient!, $"{_graphEduBaseUrl}/classes/{classId}/members");
                var teacherWrapper = await GraphGetRequest<EducationUserWrapper>(_httpClient!, $"{_graphEduBaseUrl}/classes/{classId}/teachers");
                var teacherIds = new HashSet<string>();
                foreach (var teacher in teacherWrapper!.Users)
                {
                    teacherIds.Add(teacher.UserId);
                }

                return studentWrapper!.Users.Where(user => !teacherIds.Contains(user.UserId)).ToList();
            }
            catch (Exception e)
            {
                _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, e, $"MS Graph error fetching EDU students for class with ID: {classId}.");
                return null;
            }
        }

        public async Task<Dictionary<string, string>> GetStudentSisIdDict(string classId)
        {
            var users = await GetStudentsByClass(classId);
            var sisIdDict = new Dictionary<string, string>();

            if (users is not null)
            {
                foreach (var user in users)
                {
                    string sisId;
                    if (user.Student is null || user.Student?.ExternalId is null)
                    {
                        sisId = "";
                    }
                    else sisId = user.Student.ExternalId;
                    sisIdDict.Add(user.UserId, sisId); 
                }
            }
            return sisIdDict;
        }

        public async Task<List<AssignmentEntity>?> GetAssignmentsByClass(string classId) 
        {
            try
            {
                var wrapper = await GraphGetRequest<AssignmentsWrapper>(_httpClient!, $"{_graphEduBetaUrl}/classes/{classId}/assignments/?$expand=*");
                return wrapper!.Assignments;
            } catch (Exception e)
            {
                _logger.Log(Microsoft.Extensions.Logging.LogLevel.Error, e, "MS Graph error fetching EDU assignments.");
                return null;
            }
        }

        private async Task<AssignmentWithSubmissions> GetSubmissionsByAssignmentId(string classId, string assignmentId)
        {
            var wrapper = await GraphGetRequest<SubmissionsWrapper>(
                _httpClient!,
                $"{_graphEduBaseUrl}/classes/{classId}/assignments/{assignmentId}/submissions?$expand=outcomes"
            );

            var assignmentWithSubmissions = new AssignmentWithSubmissions
            {
                AssignmentId = assignmentId,
                Submissions = wrapper!.Submissions
            };
            return assignmentWithSubmissions;
        }

        public async Task<Dictionary<string, List<SubmissionEntity>>> GetAllAssignmentsWithSubmissions(string classId, List<string> assignmentIdList)
        {
            var taskList = new List<Task<AssignmentWithSubmissions>>();
            foreach (var id in assignmentIdList)
            {
                var task = GetSubmissionsByAssignmentId(classId, id);
                taskList.Add(task);
            }

            var submissionsDict = new Dictionary<string, List<SubmissionEntity>>();
            var assignmentsWithSubmissions = (await Task.WhenAll(taskList)).ToList();

            foreach (var entity in assignmentsWithSubmissions)
            {
                submissionsDict.Add(entity.AssignmentId, entity.Submissions);
            }
            return submissionsDict;
        }


    }
}
