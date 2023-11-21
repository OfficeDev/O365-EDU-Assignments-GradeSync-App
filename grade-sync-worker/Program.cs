// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using GradeSyncApi.Services.Storage;
using GradeSyncApi.Services.Graph;
using GradeSyncApi.Services.OneRoster;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddScoped<ITableStorageService, TableStorageService>();
        services.AddScoped<IGraphService, GraphService>();
        services.AddScoped<IOneRosterService, OneRosterService>();
        services.AddSingleton<IDirectoryRoleService, DirectoryRoleService>();
    })
    .Build();

host.Run();
