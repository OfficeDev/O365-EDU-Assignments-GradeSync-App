// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
namespace GradeSyncApi.Services.Graph
{
    public interface IDirectoryRoleService
    {
        HashSet<string> GetAdminRoleIds();
    }
}

