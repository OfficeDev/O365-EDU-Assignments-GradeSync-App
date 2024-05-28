// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using Newtonsoft.Json;

namespace GradeSyncApi.Services.OneRoster
{
    public enum OneRosterResourceType
    {
        LineItem,
        LineItemResult
    }

    public abstract class PaginatedWrapperBase<T>
    {
        public abstract void CombinePages(List<T> wrappers);
    }

    public class SessionWrapper : PaginatedWrapperBase<SessionWrapper>
    {
        [JsonProperty("academicSessions")]
        public List<AcademicSession> Sessions { get; set; }

        public override void CombinePages(List<SessionWrapper> wrappers)
        {
            var combinedSessions = new List<AcademicSession>();
            foreach (var wrapper in wrappers)
            {
                combinedSessions.AddRange(wrapper.Sessions);
            }

            Sessions = combinedSessions;
        }
    }

    public class CategoryWrapper : PaginatedWrapperBase<CategoryWrapper>
    {
        [JsonProperty("categories")]
        public List<Category> Categories { get; set; }

        public override void CombinePages(List<CategoryWrapper> wrappers)
        {
            var combinedCategories = new List<Category>();
            foreach (var wrapper in wrappers)
            {
                combinedCategories.AddRange(wrapper.Categories);
            }

            Categories = combinedCategories;
        }
    }

    public class EnrollmentWrapper : PaginatedWrapperBase<EnrollmentWrapper>
    {
        [JsonProperty("enrollments")]
        public List<Enrollment> Enrollments { get; set; }

        public override void CombinePages(List<EnrollmentWrapper> wrappers)
        {
            var combinedEnrollments = new List<Enrollment>();
            foreach (var wrapper in wrappers)
            {
                combinedEnrollments.AddRange(wrapper.Enrollments);
            }

            Enrollments = combinedEnrollments;
        }
    }

    public class UserWrapper : PaginatedWrapperBase<UserWrapper>
    {
        [JsonProperty("users")]
        public List<OneRosterUser> Users { get; set; }

        public override void CombinePages(List<UserWrapper> wrappers)
        {
            var combinedUsers = new List<OneRosterUser>();
            foreach (var wrapper in wrappers)
            {
                combinedUsers.AddRange(wrapper.Users);
            }

            Users = combinedUsers;
        }
    }

    public class OneRosterClassWrapper : PaginatedWrapperBase<OneRosterClassWrapper>
    {
        [JsonProperty("classes")]
        public List<OneRosterClass> Classes { get; set; }

        public override void CombinePages(List<OneRosterClassWrapper> wrappers)
        {
            var combinedClasses = new List<OneRosterClass>();
            foreach (var wrapper in wrappers)
            {
                combinedClasses.AddRange(wrapper.Classes);
            }

            Classes = combinedClasses;
        }
    }

    public class ClassGroupWrapper
    {
        [JsonProperty("classGroup")]
        public ClassGroup ClassGroup { get; set; }
    }

    public class ClassGroup
    {
        [JsonProperty("sourcedId")]
        public string Id { get; set; }

        [JsonProperty("classes")]
        public List<IdTypeMapping> Classes { get; set; }
    }

    public class Enrollment
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("user")]
        public IdTypeMapping User { get; set; }

        [JsonProperty("class")]
        public IdTypeMapping Class { get; set; }
    }

    public class AcademicSession
    {
        [JsonProperty("sourcedId")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("startDate")]
        public string StartDate { get; set; }

        [JsonProperty("endDate")]
        public string EndDate { get; set; }

        [JsonIgnore]
        public DateTime StartTime { get; set; }

        [JsonIgnore]
        public DateTime EndTime { get; set; }
    }

    public class Category
    {
        public Category() { }

        [JsonProperty("sourcedId")]
        public string Id { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("metadata")]
        public Metadata? Metadata { get; set; }
    }

    public class TokenWrapper
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }
    }

    public class Metadata
    {
        [JsonProperty("classId")]
        public string? ClassSourcedId { get; set; }
    }

    public class IdTypeMapping
    {
        // general binding class you can use for many different fields that are just reprsented by sourcedId and type
        public IdTypeMapping(string id, string typeName)
        {
            Id = id;
            Type = typeName;
        }

        [JsonProperty("sourcedId")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }
    }

    public class LineItemWrapper
    {
        public LineItemWrapper(LineItem item)
        {
            LineItem = item;
        }

        [JsonProperty("lineItem")]
        public LineItem LineItem { get; set; }
    }

    public class LineItem
    {
        public LineItem() { }

        [JsonProperty("sourcedId")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string? Description { get; set; }

        [JsonProperty("dueDate")]
        public string DueDate { get; set; }

        [JsonProperty("assignDate")]
        public string AssignDate { get; set; }

        [JsonProperty("resultValueMin")]
        public double ResultValueMin { get; set; }

        [JsonProperty("resultValueMax")]
        public double ResultValueMax { get; set; }

        [JsonProperty("class")]
        public IdTypeMapping Class { get; set; }

        [JsonProperty("gradingPeriod")]
        public IdTypeMapping? GradingPeriod { get; set; }

        [JsonProperty("category")]
        public IdTypeMapping Category { get; set; }
    }

    public class IdField
    {
        public IdField(string id)
        {
            Id = id;
        }

        [JsonProperty("sourcedId")]
        public string Id { get; set; }
    }

    public class ResultWrapper
    {
        public ResultWrapper(Result result)
        {
            Result = result;
        }

        [JsonProperty("result")]
        public Result Result { get; set; }
    }

    public class Result
    {
        public Result() { }

        [JsonProperty("sourcedId")]
        public string Id { get; set; }

        [JsonProperty("scoreStatus")]
        public string ScoreStatus { get; set; }

        [JsonProperty("scoreDate")]
        public string ScoreDate { get; set; }

        [JsonProperty("score")]
        public double? Score { get; set; }

        [JsonProperty("student")]
        public IdField Student { get; set; }

        [JsonProperty("lineItem")]
        public IdField LineItem { get; set; }
    }

    public class OneRosterUser
    {
        [JsonProperty("sourcedId")]
        public string Id { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("email")]
        public string Email { get; set; }

        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("givenName")]
        public string GivenName { get; set; }

        [JsonProperty("familyName")]
        public string FamilyName { get; set; }
    }

    public class OneRosterClass
    {
        [JsonProperty("sourcedId")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("classCode")]
        public string ClassCode { get; set; }
    }
}
