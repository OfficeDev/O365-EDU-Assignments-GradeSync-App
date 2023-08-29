using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using GradeSyncApi.Services.Graph.JsonEntities;
using Newtonsoft.Json;

namespace GradeSyncApi.Services.OneRoster
{
    public static class OneRosterHttpClient
    {
        public static async Task<List<T>> PaginatedGetRequest<T>(
            HttpClient client,
            string url,
            string token,
            int pageSize,
            Tuple<string, string>? additionalFilter = null
        )
        {
            var pageList = new List<T>();
            string? currentUrl = $"{url}?limit={pageSize}&offset=0";
            if (additionalFilter is not null) {
                // additional filters MUST be url encoded, because OneRoster API will autoencode them if you don't, and then when you
                // attempt to run the pagination logic, it will fetch duplicate pages because the currentUrl will not match the lastUrl due to different encoding,
                // even though they hit the same resource in OneRoster

                // they don't encode the entire filter query params, they encode it arbitrarily like this, for whatever reason...
                var encodedFilter = Uri.EscapeDataString($"{additionalFilter.Item1}='{additionalFilter.Item2}'");
                currentUrl = $"{currentUrl}&filter={encodedFilter}";
            }

            while (currentUrl is not null)
            {
                var getRequest = await GetRequest(client, currentUrl, token);
                var wrapper = JsonConvert.DeserializeObject<T>(getRequest.Item1);
                if (wrapper is not null)
                {
                    pageList.Add(wrapper);
                }

                currentUrl = TryGetNextLink(getRequest.Item2, currentUrl);
            }

            return pageList;
        }

        public static async Task<Tuple<string, HttpResponseMessage>> GetRequest(HttpClient client, string url, string token)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var res = await client.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();

            try
            {
                res.EnsureSuccessStatusCode();
                return new Tuple<string, HttpResponseMessage>(content, res);
            } catch (Exception)
            {
                throw new ApplicationException(content);
            }
        }

        public static async Task<T> PutRequest<T>(HttpClient client, string url, string token, object reqContent)
        {
            var serialized = JsonConvert
                .SerializeObject(reqContent, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            var requestContent = new StringContent(serialized, Encoding.UTF8, "application/json");
            var req = new HttpRequestMessage(HttpMethod.Put, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = requestContent;

            var res = await client!.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();
            Console.WriteLine(content);

            try
            {
                res.EnsureSuccessStatusCode();
                return JsonConvert.DeserializeObject<T>(content)!;
            }
            catch (HttpRequestException)
            {
                throw new ApplicationException(content);
            }
        }

        public static string? TryGetNextLink(HttpResponseMessage res, string currentLinkUrl)
        {
            string? link = null;
            IEnumerable<string> linkHeaders;

            if (res.Headers.TryGetValues("Link", out linkHeaders))
            {
                var linkHeaderItems = linkHeaders?.FirstOrDefault()?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                var nextLink = linkHeaderItems?.FirstOrDefault(x => x.EndsWith("rel=\"next\"", StringComparison.OrdinalIgnoreCase));
                var lastLink = linkHeaderItems?.FirstOrDefault(x => x.EndsWith("rel=\"last\"", StringComparison.OrdinalIgnoreCase));

                if (nextLink is not null && lastLink is not null)
                {
                    var nextLinkUrl = nextLink.Trim().Substring(1, nextLink.IndexOf(">", StringComparison.OrdinalIgnoreCase) - 1);
                    var lastLinkUrl = lastLink.Trim().Substring(1, nextLink.IndexOf(">", StringComparison.OrdinalIgnoreCase) - 1);

                    if (currentLinkUrl == lastLinkUrl) return null;
                    link = nextLinkUrl;
                }
            }

            return link;
        }
    }
}
