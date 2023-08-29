using System;
using System.ComponentModel;
using System.Text;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IdentityModel.Tokens.Jwt;
using Newtonsoft.Json;
using GradeSyncApi.Services.Storage;

namespace GradeSyncApi.Services.OneRoster
{
    public class OneRosterService : IOneRosterService
    {
        private readonly int _defaultPageSize = 100;
        private static readonly SemaphoreSlim _accessTokenLock = new SemaphoreSlim(1, 1);

        private readonly IConfiguration _configuration;
        private HttpClient? _httpClient;
        private OneRosterConnectionEntity? _apiCreds;
        private string? _accessToken;
        private DateTime? _accessTokenExpiresAt;
        private string? _currentGradingPeriodId;

        public OneRosterService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task InitApiConnection(OneRosterConnectionEntity? connectionEntity)
        {
            if (connectionEntity is not null)
            {
                _apiCreds = connectionEntity;
            }

            try
            {
                var httpClient = new HttpClient();

                var values = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                };
                var content = new FormUrlEncodedContent(values);

                var authString = $"{_apiCreds!.ClientId}:{_apiCreds.ClientSecret}";
                var base64AuthString = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(authString));

                var req = new HttpRequestMessage(HttpMethod.Post, _apiCreds.OAuth2TokenUrl);
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64AuthString);
                req.Content = content;

                var res = await httpClient.SendAsync(req);
                res.EnsureSuccessStatusCode();

                var resBody = await res.Content.ReadAsStringAsync();
                var tokenWrapper = JsonConvert.DeserializeObject<TokenWrapper>(resBody);

                _accessToken = tokenWrapper!.AccessToken;
                SetExpTime(_accessToken);
                
                if (_httpClient is null)
                {
                    _httpClient = new HttpClient();

                    var vendorAuthKey = _configuration.GetValue<string>("VendorAuthHeader");
                    if (vendorAuthKey is not null && vendorAuthKey != "")
                    {
                        _httpClient.DefaultRequestHeaders.Add("x-vendor-authorization", vendorAuthKey);
                    }
                    _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                }
            } catch (Exception)
            {
                throw new ApplicationException("Could not validate OneRoster API credentials");
            }
        }

        private void SetExpTime(string jwt)
        {
            var token = new JwtSecurityToken(jwt);
            var expiresAt = token.ValidTo;
            _accessTokenExpiresAt = expiresAt.AddMinutes(-5);
        }

        private async Task<string> GetOrRefreshAccessToken()
        {
            var timeNow = DateTime.UtcNow;
            if (timeNow < _accessTokenExpiresAt)
            {
                return _accessToken!;
            } else
            {
                await _accessTokenLock.WaitAsync();
                try
                {
                    if (timeNow >= _accessTokenExpiresAt)
                    {
                        await InitApiConnection(null);
                    }
                } finally
                {
                    _accessTokenLock.Release();
                }  
            }
            return _accessToken!;
        }

        public async Task<LineItem?> CreateLineItem(string sourcedId, AssignmentEntity assignmentEntity, string classExternalId, string oneRosterConnectionId)
        {
            if (assignmentEntity.MaxPoints is null) throw new ApplicationException("Can't create line item assignment that doesn't have maximum points.");

            var lineItem = new LineItem
            {
                Id = sourcedId,
                Title = assignmentEntity.DisplayName,
                Description = assignmentEntity.Desc,
                DueDate = assignmentEntity.DueTimestamp,
                AssignDate = assignmentEntity.AssignedTimestamp,
                ResultValueMin = 0.0,
                ResultValueMax = (double)assignmentEntity.MaxPoints,
                Class = new IdTypeMapping(classExternalId, "class"),
                // add grading period
                GradingPeriod = new IdTypeMapping(_currentGradingPeriodId!, "academicSession")
            };

            // add category if it is specified
            var categoryDict = assignmentEntity.DeserializeCategoryDict();
            if (categoryDict is not null)
            {
                if (categoryDict.TryGetValue(oneRosterConnectionId, out CategoryMapping value))
                {
                    if (value.CatId != "none")
                    {
                        lineItem.Category = new IdTypeMapping(value.CatId, "category");
                    }
                }
            }

            var wrapper = new LineItemWrapper(lineItem);
            var url = $"{_apiCreds!.OneRosterBaseUrl}/lineItems/{lineItem.Id}";
            var token = await GetOrRefreshAccessToken();
            var createdLineItemWrapper = await OneRosterHttpClient.PutRequest<LineItemWrapper>(_httpClient!, url, token, wrapper);
            return createdLineItemWrapper?.LineItem;
        }

        public async Task<bool> OneRosterResourceExists(string sourcedId, OneRosterResourceType resourceType)
        {
            string url = "";
            switch (resourceType)
            {
                case OneRosterResourceType.LineItem:
                    url = $"{_apiCreds!.OneRosterBaseUrl}/lineItems/{sourcedId}";
                    break;
                case OneRosterResourceType.LineItemResult:
                    url = $"{_apiCreds!.OneRosterBaseUrl}/results/{sourcedId}";
                    break;
            }

            var token = await GetOrRefreshAccessToken();
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var res = await _httpClient!.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();

            try
            {
                res.EnsureSuccessStatusCode();
                return true;
            }
            catch (HttpRequestException)
            {
                if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
                throw new ApplicationException(content);
            }
        }

        public async Task<List<Category>> GetActiveCategories()
        {
            var url = $"{_apiCreds!.OneRosterBaseUrl}/categories";
            var token = await GetOrRefreshAccessToken();
            var paginated = await OneRosterHttpClient.PaginatedGetRequest<CategoryWrapper>(_httpClient!, url, token, _defaultPageSize);
            var combined = paginated.FirstOrDefault();
            combined!.CombinePages(paginated);

            return combined.Categories.Where(category => category.Status == "active").ToList();
        }

        public async Task<List<Enrollment>> GetEnrollmentsByClass(string classSourcedId)
        {
            var url = $"{_apiCreds!.OneRosterBaseUrl}/enrollments";
            var token = await GetOrRefreshAccessToken();
            var paginated = await OneRosterHttpClient
                .PaginatedGetRequest<EnrollmentWrapper>(
                    _httpClient!,
                    url,
                    token,
                    _defaultPageSize,
                    new Tuple<string, string>("classSourcedId", classSourcedId)
                );

            var combined = paginated.FirstOrDefault();
            combined!.CombinePages(paginated);

            return combined.Enrollments.Where(enrollment => enrollment.Status == "active").ToList();
        }

        public async Task<List<OneRosterUser>> GetStudentUsersByClass(string classSourcedId)
        {
            var url = $"{_apiCreds!.OneRosterBaseUrl}/classes/{classSourcedId}/students";
            var token = await GetOrRefreshAccessToken();
            var paginated = await OneRosterHttpClient
                .PaginatedGetRequest<UserWrapper>(
                    _httpClient!,
                    url,
                    token,
                    _defaultPageSize
                );

            var combined = paginated.FirstOrDefault();
            combined!.CombinePages(paginated);
            return combined.Users;
        }

        public async Task<List<OneRosterUser>> GetTeachersMatchedByEmail(string? adUserEmail = null)
        {
            var url = $"{_apiCreds!.OneRosterBaseUrl}/teachers";
            var token = await GetOrRefreshAccessToken();
            var paginated = await OneRosterHttpClient
                .PaginatedGetRequest<UserWrapper>(
                    _httpClient!,
                    url,
                    token,
                    _defaultPageSize
                );

            var combined = paginated.FirstOrDefault();
            combined!.CombinePages(paginated);

            if (adUserEmail is null) return combined.Users; // return all teachers if no Active Directory email was specified
            return combined.Users.Where(user => user.Username == adUserEmail || user.Email == adUserEmail).ToList();
        }

        public async Task<ClassGroup> GetClassGroup(string classGroupSourcedId)
        {
            var url = $"{_apiCreds!.OneRosterBaseUrl}/classGroups/{classGroupSourcedId}";
            var token = await GetOrRefreshAccessToken();
            var req = await OneRosterHttpClient.GetRequest(_httpClient!, url, token);
            var res = req.Item2;

            Console.WriteLine(req.Item1);

            try
            {
                res.EnsureSuccessStatusCode();
                var wrapper = JsonConvert.DeserializeObject<ClassGroupWrapper>(req.Item1);
                return wrapper!.ClassGroup;
            } catch (Exception)
            {
                throw new ApplicationException($"Error fetching OneRoster classGroups with classGroupSourcedId={classGroupSourcedId}");
            }
        }

        public async Task ValidateClassGroups()
        {
            var url = $"{_apiCreds!.OneRosterBaseUrl}/classGroups?offset=0&limit=10";
            var token = await GetOrRefreshAccessToken();
            var getReq = await OneRosterHttpClient.GetRequest(_httpClient!, url, token);
            var res = getReq.Item2;

            Console.WriteLine(getReq.Item1);

            try
            {
                res.EnsureSuccessStatusCode();
            } catch (Exception)
            {
                throw new ApplicationException("OneRoster /classGroups endpoint either doesn't exist or returned an error.");
            }
        }

        public async Task<List<OneRosterClass>> GetClasses(string? teacherSourcedId, bool isAdmin)
        {
            string url;
            if (isAdmin)
            {
                url = $"{_apiCreds!.OneRosterBaseUrl}/classes";
            }
            else
            {
                url = $"{_apiCreds!.OneRosterBaseUrl}/teachers/{teacherSourcedId}/classes";
            }
            
            var token = await GetOrRefreshAccessToken();
            var paginated = await OneRosterHttpClient
                .PaginatedGetRequest<OneRosterClassWrapper>(
                    _httpClient!,
                    url,
                    token,
                    _defaultPageSize
                );

            var combined = paginated.FirstOrDefault();
            combined!.CombinePages(paginated);
            return combined.Classes;
        }

        public async Task GetCurrentGradingPeriod()
        {
            try
            {
                var url = $"{_apiCreds!.OneRosterBaseUrl}/academicSessions";
                var token = await GetOrRefreshAccessToken();
                var paginated = await OneRosterHttpClient.PaginatedGetRequest<SessionWrapper>(_httpClient!, url, token, _defaultPageSize);
                var combined = paginated.FirstOrDefault();
                combined!.CombinePages(paginated);

                var timeNow = DateTime.UtcNow;
                DateTime maxDate = DateTime.MinValue;
                string maxDateId = "";
                foreach (var session in combined.Sessions)
                {
                    if (session.Type == "gradingPeriod")
                    {
                        session.StartTime = DateTime.Parse(session.StartDate);
                        session.EndTime = DateTime.Parse(session.EndDate);

                        if (timeNow >= session.StartTime && timeNow <= session.EndTime)
                        {
                            _currentGradingPeriodId = session.Id;
                            break;
                        }

                        if (session.EndTime >= maxDate)
                        {
                            maxDate = session.EndTime;
                            maxDateId = session.Id;
                        }
                    }
                }

                if (_currentGradingPeriodId is null)
                {
                    _currentGradingPeriodId = maxDateId;
                    Console.WriteLine($"Current max grading period end date: {maxDate.Date.ToShortDateString()}");
                }
            } catch (Exception)
            {
                throw new ApplicationException("Error getting current OneRoster Term/academicSession");
            }
        }

        public async Task<Result?> CreateLineItemResult(SubmissionEntity submission, string studentSisId, string assignmentId, string? subClassExternalId = null)
        {
            string errorMessage = $"Graph API Submission has missing points outcome. Student SIS Id: {studentSisId} Submission Id: {submission.SubmissionId}";
            var result = new Result
            {
                Id = submission.SubmissionId,
                ScoreStatus = "fully graded",
                ScoreDate = submission.ReturnedDateTime
            };

            foreach (var outcome in submission.Outcomes)
            {
                if (outcome.OutcomeType == OutcomeType.PointsOutcome)
                {
                    if (outcome.Points is null || outcome.Points?.Points is null) throw new ApplicationException(errorMessage);
                    result.Score = outcome.Points.Points;
                    break;
                }
            }

            if (result.Score is null) throw new ApplicationException(errorMessage);
            result.Student = new IdField(studentSisId);

            // if we have a subClassExternalId, that means the connection is groupEnabled, and we need to create the result based on the
            // assignment + classId composite sourcedId
            if (subClassExternalId is null)
            {
                result.LineItem = new IdField(assignmentId);
            }
            else
            {
                result.LineItem = new IdField($"{assignmentId}-{subClassExternalId}");
            }
            
            var wrapper = new ResultWrapper(result);
            var url = $"{_apiCreds!.OneRosterBaseUrl}/results/{submission.SubmissionId}";
            var token = await GetOrRefreshAccessToken();

            try
            {
                var createdResultWrapper = await OneRosterHttpClient.PutRequest<ResultWrapper>(_httpClient!, url, token, wrapper);
                return createdResultWrapper?.Result;
            }
            catch (Exception e)
            {
                throw new ApplicationException($"Submission with ID: {submission.SubmissionId} failed on OneRoster lineItemResult request: {e.Message}");
            }
        }
    }
}
