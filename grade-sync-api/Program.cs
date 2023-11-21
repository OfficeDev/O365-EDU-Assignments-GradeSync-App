// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging.ApplicationInsights;
using Microsoft.Identity.Web;

using GradeSyncApi.Services.Storage;
using GradeSyncApi.Services.Graph;
using GradeSyncApi.Services.OneRoster;

var builder = WebApplication.CreateBuilder(args);

/* 
 * Un-comment if you want logging to go to the console for local dev
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
*/

// Add services to the container
builder.Services.AddScoped<ITableStorageService, TableStorageService>();
builder.Services.AddScoped<IGraphService, GraphService>();
builder.Services.AddScoped<IMessageQueueService, MessageQueueService>();
builder.Services.AddSingleton<IDirectoryRoleService, DirectoryRoleService>();
builder.Services.AddScoped<IOneRosterService, OneRosterService>();

// add AAD token auth
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

// add app insights config (comment-out to turn off for local dev)
builder.Logging.AddApplicationInsights(configureTelemetryConfiguration: (config) =>
    config.ConnectionString = builder.Configuration.GetValue<string>("APPLICATIONINSIGHTS_CONNECTION_STRING"),
    configureApplicationInsightsLoggerOptions: (options) => { }
);
builder.Logging.AddFilter<ApplicationInsightsLoggerProvider>("AppError", LogLevel.Error);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("index.html");
app.Run();
