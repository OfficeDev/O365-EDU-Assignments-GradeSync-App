using System;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection.KeyManagement;

namespace GradeSyncApi.Helpers
{
    public class ClaimsHelper
    {
        public static Dictionary<string, string> ClaimsToDict(IEnumerable<Claim> claims)
        {
            var claimsList = claims.ToList();
            var dict = new Dictionary<string, string>();

            foreach (Claim claim in claimsList)
            {
                if (claim.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")
                {
                    dict.Add("sub", claim.Value);
                }
                else if (claim.Type == "http://schemas.microsoft.com/identity/claims/tenantid")
                {
                    dict.Add("tid", claim.Value);
                }
                else
                {
                    dict.Add(claim.Type, claim.Value);
                }
            }

            return dict;
        }
    }
}

