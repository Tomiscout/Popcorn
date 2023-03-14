using VideoBlazor.SearchProviders.Exceptions;
using VideoBlazor.SearchProviders.Models;
using HtmlAgilityPack;
using System;
using VideoBlazor.SearchProviders.Utils;
using System.Net;
using System.IO.Compression;
using Microsoft.AspNetCore.WebUtilities;
using VideoBlazor.Shared.Enum;
using VideoBlazor.Shared.Models;

namespace VideoBlazor.SearchProviders.Providers.Linkomanija
{
    public class LinkomanijaProvider : ISearchProvider, ITorrentDownloader
    {
        public SearchSourceType Source => SearchSourceType.Linkomanija;
        public bool Imdb { get; } = true;

        private const string _cookieName = "PHPSESSID";
        private const string _indexUrl = "https://www.linkomanija.net/login.php";
        private const string _loginUrl = "https://www.linkomanija.net/takelogin.php";
        private const string _searchUrl = "https://www.linkomanija.net/browse.php";

        //TODO use 1.1.1.1 DNS
        private HttpClientHandler _clientHandler => new HttpClientHandler() { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };

        private const string _username = "tomazasxxl";
        private const string _password = "Narkevicius";

        private string _cookie = null;

        public async Task<SearchQueryResult> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(_cookie))
            {
                await Login();
            }

            using HttpClient client = new HttpClient(_clientHandler); //TODO proper httpclient handling
            Dictionary<string, string> queryParameters = new Dictionary<string, string>()
            {
                { "search", query },
                { "incldead", "0" }
            };
            var url = new Uri(QueryHelpers.AddQueryString(_searchUrl, queryParameters)).ToString();
            var response = await AuthenticatedGetRequest(client, url);
            var html = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var searchResults = new List<SearchResult>();
            var tableRows = doc.DocumentNode.SelectNodes("//*[@id='content']/form/table[2]/tr");
            if (tableRows.Count() == 0)
            {
                return new SearchQueryResult()
                {
                    Results = searchResults,
                    Source = Source,
                    Success = true
                };
            }

            try
            {
                for (var i = 1; i < tableRows.Count(); i++)
                {
                    searchResults.Add(ParseTableRow(tableRows[i]));
                }
            } catch(Exception e)
            {
                return new SearchQueryResult()
                {
                    Results = searchResults,
                    Source = Source,
                    Error = e,
                    ErrorMessage = "Failed to parse search results",
                    Success = false
                };
            }

            return new SearchQueryResult()
            {
                Results = searchResults,
                Source = Source,
                Success = true
            };
        }

        public async Task<SearchQueryResult> SearchImdb(string imdb, string title)
        {
            return await Search(title);
        }

        public async Task<string> DownloadTorrent(string url)
        {
            if (string.IsNullOrWhiteSpace(_cookie)) await Login();

            using HttpClient client = new HttpClient(_clientHandler);
            try
            {
                var response = await AuthenticatedGetRequest(client, url);
                var torrentFileName = WebUtility.UrlDecode(url.Substring(url.IndexOf("name=") + 5));
                var path = Path.Join(TorrentUtils.GetTorrentsFolder(), torrentFileName);
                File.Delete(path);
                await using var fs = new FileStream(path, FileMode.CreateNew);
                await response.Content.CopyToAsync(fs);
                return path;
            } catch(Exception e)
            {
                throw e;
            }
        }

        private async Task<HttpResponseMessage> AuthenticatedGetRequest(HttpClient client, string url)
        {
            var response = await GetRequest(client, url);

            if (response.RequestMessage.RequestUri.AbsolutePath.Contains("login.php"))
            {
                await Login();
                response = await GetRequest(client, url);
            }
            return response;
        }

        private async Task<HttpResponseMessage> GetRequest(HttpClient client, string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url) { };
            AddDefaultHeaders(req);
            req.Headers.Add("Referer", "https://www.linkomanija.net/browse.php");
            req.Headers.Add("Cookie", _cookie);
            return await client.SendAsync(req);
        }

        private SearchResult ParseTableRow(HtmlNode row)
        {
            var genre = row.ChildNodes[1].FirstChild.FirstChild.Attributes["title"].Value;
            var titleDiv = row.ChildNodes[3];
            var uploadedDate = row.ChildNodes[9].InnerText.Insert(10, " ");

            //This is neccesary since freeleech and (NAUJAS) tags may fuck up order
            var downloadImageNode = titleDiv.ChildNodes
                .Where(c => c.Name == "a" && c.FirstChild is not null)
                .Select(c => c.FirstChild)
                .Where(c => c.Name == "img" && c.Attributes["title"]?.Value == "Parsisiųsti")
                .FirstOrDefault()?.ParentNode;
            if(downloadImageNode is null)
            {
                //TODO Failed parsing download link
            }

            return new SearchResult()
            {
                Source = Source,
                Title = titleDiv.FirstChild.InnerText,
                TorrentPageUrl = titleDiv.FirstChild.Attributes["href"].Value,
                Description = titleDiv.ChildNodes["span"].InnerText,
                TorrentFileLink = downloadImageNode.Attributes["href"].Value,
                FilesCount = int.Parse(row.ChildNodes[5].InnerText),
                UploadedDate = DateTime.Parse(uploadedDate),
                Seeds = int.Parse(row.ChildNodes[15].InnerText),
                Peers = int.Parse(row.ChildNodes[17].InnerText)
            };
        }

        private async Task Login()
        {
            using HttpClient client = new HttpClient(_clientHandler); //TODO proper httpclient handling
            //Load website to get session id from cookie

            string cookie = null;
            try
            {
                var response = await client.GetAsync(_indexUrl);
                cookie = GetCookie(response);
                if (string.IsNullOrWhiteSpace(cookie)) throw new Exception("Cookie is null"); //TODO proper exception
            }
            catch (Exception e)
            {
                throw new HttpRequestException("Failed to get token for Linkomanija", e);
            }

            //Login request
            var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>()
            {
                { "username", _username },
                { "password", _password },
                { "commit", "Prisijungti" }
            });
            var req = new HttpRequestMessage(HttpMethod.Post, _loginUrl) { Content = loginForm };
            AddDefaultHeaders(req);
            req.Headers.Add("Referer", "https://www.linkomanija.net/login.php");
            req.Headers.Add("Cookie", cookie);


            try
            {
                var res = await client.SendAsync(req);
                if(res.RequestMessage.RequestUri.ToString().Contains("klaida")) {
                    throw new SearchFailedException("Authorization failed. Wrong username or password");
                }
            }
            catch (SearchFailedException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new HttpRequestException("Failed to reach linkomanija", e);
            }
            _cookie = cookie;
        }

        private void AddDefaultHeaders(HttpRequestMessage req)
        {
            req.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            req.Headers.Add("Accept-Encoding", "gzip, deflate, br");
            req.Headers.Add("Accept-Language", "en-US,en;q=0.8");
            req.Headers.Add("Cache-Control", "max-age=0");
            req.Headers.Add("Connection", "keep-alive");
            req.Headers.Add("Host", "www.linkomanija.net");
            req.Headers.Add("Origin", "https://www.linkomanija.net");
            req.Headers.Add("Sec-Fetch-Dest", "document");
            req.Headers.Add("Sec-Fetch-Mode", "navigate");
            req.Headers.Add("Sec-Fetch-Site", "same-origin");
            req.Headers.Add("Sec-Fetch-User", "?1");
            req.Headers.Add("Sec-GPC", "1");
            req.Headers.Add("Upgrade-Insecure-Requests", "1");
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36");
        }

        private string GetCookie(HttpResponseMessage message)
        {
            if (!message.Headers.TryGetValues("Set-Cookie", out var setCookie)) return null;
            var setCookieString = setCookie.Single();
            var cookieTokens = setCookieString.Split(';');
            var sessionCookie = cookieTokens.FirstOrDefault(c => c.StartsWith(_cookieName));
            if (sessionCookie is null) return null;
            return sessionCookie;
        }
    }
}
