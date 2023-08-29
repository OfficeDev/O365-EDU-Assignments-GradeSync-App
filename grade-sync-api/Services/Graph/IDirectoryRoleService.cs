using System;

namespace GradeSyncApi.Services.Graph
{
    public interface IDirectoryRoleService
    {
        HashSet<string> GetAdminRoleIds();
    }
}

