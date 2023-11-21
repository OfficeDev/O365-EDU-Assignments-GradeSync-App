// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace GradeSyncApi.Services.Graph
{
    public class DirectoryRoleService : IDirectoryRoleService
    {
        private HashSet<string> _adminRoleIds;

        public DirectoryRoleService()
        {
            InitIdSet();
        }

        public HashSet<string> GetAdminRoleIds()
        {
            if (_adminRoleIds is null)
            {
                InitIdSet();
            }
            return _adminRoleIds!;
        }

        private void InitIdSet()
        {
            // https://github.com/Azure/azure-docs-powershell-azuread/blob/main/azureadps-2.0/AzureAD/Get-AzureADDirectoryRoleTemplate.md

            _adminRoleIds = new HashSet<string>();
            _adminRoleIds.Add("729827e3-9c14-49f7-bb1b-9608f156bbb8"); // Helpdesk Administrator
            _adminRoleIds.Add("f023fd81-a637-4b56-95fd-791ac0226033"); // Service Support Administrator
            _adminRoleIds.Add("b0f54661-2d74-4c50-afa3-1ec803f12efe"); // Billing Administrator
            _adminRoleIds.Add("b5468a13-3945-4a40-b0b1-5d78c2676bbf"); // Mailbox Administrator
            _adminRoleIds.Add("29232cdf-9323-42fd-ade2-1d097af3e4de"); // Exchange Service Administrator
            _adminRoleIds.Add("75941009-915a-4869-abe7-691bff18279e"); // Lync Service Administrator
            _adminRoleIds.Add("fe930be7-5e62-47db-91af-98c3a49a38b1"); // User Administrator
            _adminRoleIds.Add("62e90394-69f5-4237-9190-012177145e10"); // Company Administrator (e.g. Global Administrator)
            _adminRoleIds.Add("eb1d8c34-acf5-460d-8424-c1f1a6fbdb85"); // AdHoc License Administrator
            _adminRoleIds.Add("f28a1f50-f6e7-4571-818b-6a12f2af6b6c"); // SharePoint Service Administrator
            _adminRoleIds.Add("9f06204d-73c1-4d4c-880a-6edb90606fd8"); // Device Administrators
            _adminRoleIds.Add("17315797-102d-40b4-93e0-432062caca18"); // Compliance Administrator
            _adminRoleIds.Add("9b895d92-2cd3-44c7-9d02-a6ac2d5ea5c3"); // Application Administrator
            _adminRoleIds.Add("194ae4cb-b126-40b2-bd5b-6091b380977d"); // Security Administrator
            _adminRoleIds.Add("e8611ab8-c189-46e8-94e1-60213ab1f814"); // Privileged Role Administrator
            _adminRoleIds.Add("158c047a-c907-4556-b7ef-446551a6b5f7"); // Application Proxy Service Administrator
        }
    }
}

