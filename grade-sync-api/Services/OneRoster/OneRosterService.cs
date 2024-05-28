// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
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

                var res = await _httpClient.SendAsync(req);
                res.EnsureSuccessStatusCode();

                var resBody = await res.Content.ReadAsStringAsync();
                var tokenWrapper = JsonConvert.DeserializeObject<TokenWrapper>(resBody);

                _accessToken = tokenWrapper!.AccessToken;
                SetExpTime(_accessToken);
            } catch (Exception e)
            {
                throw new ApplicationException($"Could not validate OneRoster API credentials: {e.Message}");
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

        public async Task<LineItem?> CreateLineItem(
            string sourcedId, 
            AssignmentEntity assignmentEntity, 
            string classExternalId, 
            string oneRosterConnectionId, 
            List<Category>? allCategories,
            OneRosterConnectionEntity connectionEntity)
        {
            if (assignmentEntity.MaxPoints is null) throw new ApplicationException("Can't create line item assignment that doesn't have maximum points.");

            IdTypeMapping? gradingPeriod = new IdTypeMapping(_currentGradingPeriodId!, "academicSession");
            if (connectionEntity.AutoSetGradingPeriod) gradingPeriod = null;

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
                GradingPeriod = gradingPeriod
            };

            // add category if it is specified
            var categoryDict = assignmentEntity.DeserializeCategoryDict();
            if (categoryDict is not null && categoryDict.TryGetValue(oneRosterConnectionId, out CategoryMapping value) && value.CatId != "none")
            {
                string passedCatId = value.CatId;
                // find the matching category for the classExternalId by matching based on title
                var selectedCat = allCategories?.Find(c => c.Id == value.CatId);
                if (selectedCat != null) {
                    var matchedCategory = allCategories?.Find(c => c.Title == selectedCat.Title && c.Metadata?.ClassSourcedId == classExternalId);
                    if (matchedCategory != null) passedCatId = matchedCategory.Id;
                }
                lineItem.Category = new IdTypeMapping(passedCatId, "category");
            } else
            {
                if (connectionEntity.AllowNoneLineItemCategory 
                    && connectionEntity.DefaultLineItemCategory is not null 
                    && connectionEntity.DefaultLineItemCategory != "none")
                {
                    lineItem.Category = new IdTypeMapping(connectionEntity.DefaultLineItemCategory, "category");
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
            var req = await OneRosterHttpClient.GetRequest(_httpClient!, url, token);
            var res = req.Item2;

            try
            {
                res.EnsureSuccessStatusCode();
                var wrapper = JsonConvert.DeserializeObject<CategoryWrapper>(req.Item1);
                return wrapper!.Categories.Where(category => category.Status == "active").ToList();
            }
            catch (Exception)
            {
                throw new ApplicationException($"Error fetching OneRoster categories.");
            }
        }

        public async Task<List<Enrollment>> GetEnrollmentsByClass(string classSourcedId)
        {
            var url = $"{_apiCreds!.OneRosterBaseUrl}/enrollments";
            var token = await GetOrRefreshAccessToken();
            var req = await OneRosterHttpClient.GetRequestFilter(_httpClient!, url, token, new Tuple<string, string>("classSourcedId", classSourcedId));
            var res = req.Item2;

            try
            {
                res.EnsureSuccessStatusCode();
                var wrapper = JsonConvert.DeserializeObject<EnrollmentWrapper>(req.Item1);
                return wrapper!.Enrollments.Where(enrollment => enrollment.Status == "active").ToList();
            }
            catch (Exception)
            {
                throw new ApplicationException($"Error fetching OneRoster enrollments for class with sourcedId: {classSourcedId}.");
            }
        }

        public async Task<List<OneRosterUser>> GetStudentUsersByClass(string classSourcedId)
        {
            var url = $"{_apiCreds!.OneRosterBaseUrl}/classes/{classSourcedId}/students";
            var token = await GetOrRefreshAccessToken();
            var req = await OneRosterHttpClient.GetRequest(_httpClient!, url, token);
            var res = req.Item2;

            try
            {
                res.EnsureSuccessStatusCode();
                var wrapper = JsonConvert.DeserializeObject<UserWrapper>(req.Item1);
                return wrapper!.Users;
            }
            catch (Exception)
            {
                throw new ApplicationException($"Error fetching OneRoster students for class.");
            }
        }

        public async Task<List<OneRosterUser>> GetTeachersMatchedByEmail(string? adUserEmail = null)
        {
            var url = $"{_apiCreds!.OneRosterBaseUrl}/teachers";
            var token = await GetOrRefreshAccessToken();
            var req = await OneRosterHttpClient.GetRequest(_httpClient!, url, token);
            var res = req.Item2;

            try
            {
                res.EnsureSuccessStatusCode();
                var wrapper = JsonConvert.DeserializeObject<UserWrapper>(req.Item1);

                if (adUserEmail is null) return wrapper!.Users; // return all teachers if no Active Directory email was specified
                return wrapper!.Users.Where(user => user.Username == adUserEmail || user.Email == adUserEmail).ToList();
            }
            catch (Exception)
            {
                throw new ApplicationException($"Error fetching OneRoster teachers.");
            }
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
            var url = $"{_apiCreds!.OneRosterBaseUrl}/classGroups";
            var token = await GetOrRefreshAccessToken();
            var getReq = await OneRosterHttpClient.GetRequest(_httpClient!, url, token, 10);
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
            var req = await OneRosterHttpClient.GetRequest(_httpClient!, url, token);
            var res = req.Item2;

            try
            {
                res.EnsureSuccessStatusCode();
                var wrapper = JsonConvert.DeserializeObject<OneRosterClassWrapper>(req.Item1);
                return wrapper!.Classes;
            }
            catch (Exception)
            {
                throw new ApplicationException($"Error fetching OneRoster classes.");
            }
        }

        public async Task GetCurrentGradingPeriod()
        {
            try
            {
                var url = $"{_apiCreds!.OneRosterBaseUrl}/academicSessions";
                var token = await GetOrRefreshAccessToken();
                var req = await OneRosterHttpClient.GetRequestFilter(_httpClient!, url, token, new Tuple<string, string>("type", "gradingPeriod"));
                var wrapper = JsonConvert.DeserializeObject<SessionWrapper>(req.Item1);

                var timeNow = DateTime.UtcNow;
                DateTime maxDate = DateTime.MinValue;
                string maxDateId = "";
                foreach (var session in wrapper!.Sessions)
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
