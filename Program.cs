using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Linq;
using System.Text.RegularExpressions;

class Program
{

    static async Task Main(string[] args)
    {
        int maxRedirect = 10;
        //string initialUrl = "https://href.li/?http://anonymz.com/?https://tinyurl.com/boraksmer";
        string initialUrl = "https://bit.ly/3DliiIe";
        //string initialUrl = "http://borakasmer.com";
        //string initialUrl = "http://bit.ly/49Qm9JN";
        //string initialUrl = "http://borakasmer.com";
        var traceResults = await TraceRedirects(initialUrl, maxRedirect);

        //traceResults.ForEach (x => Console.WriteLine($"{x.StatusCode} - {x.Url} - {x.Type}")) ;

        // Grid Başlığı
        Console.WriteLine("┌──────────────┬──────────────────────────────────────────┬───────────────────────────────────────────────┐");
        Console.WriteLine("│ Status Code  │ Requested URL                            │ Type                                          │");
        Console.WriteLine("├──────────────┼──────────────────────────────────────────┼───────────────────────────────────────────────┤");

        // Grid İçeriği
        var uniqueRedirectUrls = traceResults.Select(x => x.Url).ToList().Distinct();
        
        foreach (var result in traceResults)
        {
            Console.WriteLine($"│ {FormatColumn(result.StatusCode, 12)} │ {FormatColumn(result.Url, 40)} │ {FormatColumn(result.Type, 45)} │");
        }

        // Grid Alt Çizgisi
        Console.WriteLine("└──────────────┴──────────────────────────────────────────┴───────────────────────────────────────────────┘");
        Console.ReadLine();
    }

    static async Task<List<RedirectResult>> TraceRedirects(string url, int maxRedirect=10)
    {
        var redirectResults = new List<RedirectResult>();
        var options = new ChromeOptions();
        options.AddArgument("--headless");
        options.AddArgument("--disable-gpu");
        //options.AddArgument("--no-sandbox");

        using (var driver = new ChromeDriver(options))
        {
            try
            {
                driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(15); 
                driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);

                string currentUrl = url;
                int redirectsCount = 0;

                using (var httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
                {
                    httpClient.Timeout=TimeSpan.FromSeconds(10);
                    while (redirectsCount < maxRedirect)
                    {
                        //For Security
                        //if (redirectResults.Any(r => r.Url == currentUrl))
                        //{
                        //    throw new InvalidOperationException("Redirect loop detected.");
                        //}

                        // HTTP Redirect
                        var response = await httpClient.GetAsync(currentUrl);
                        int statusCode = (int)response.StatusCode;
                        string? redirectUrl = response.Headers.Location?.ToString();

                        redirectResults.Add(new RedirectResult
                        {
                            StatusCode = statusCode.ToString(),
                            Url = currentUrl,
                            Type = redirectsCount == 0 ? "Initial URL" : "HTTP Redirect (" + (statusCode == 301 ? "Permanent" : "Temporary") + ")"
                        });

                        if (!string.IsNullOrEmpty(redirectUrl))
                        {
                            currentUrl = redirectUrl;
                            redirectsCount++;
                            continue;
                        }

                        // Embedded Redirect
                        string embeddedUrl = GetEmbeddedRedirectUrl(currentUrl);
                        if (!string.IsNullOrEmpty(embeddedUrl))
                        {
                            redirectResults.Add(new RedirectResult
                            {
                                StatusCode = "200",
                                Url = embeddedUrl,
                                Type = "Embedded URL Redirect"                                
                            });
                            currentUrl = embeddedUrl;
                            redirectsCount++;
                            continue;
                        }

                        driver.Navigate().GoToUrl(currentUrl);

                        // Meta Refresh Redirect
                        string metaRedirectUrl = GetMetaRefreshRedirectUrl(driver.PageSource);
                        if (!string.IsNullOrEmpty(metaRedirectUrl))
                        {
                            redirectResults.Add(new RedirectResult
                            {
                                StatusCode = "206",
                                Url = metaRedirectUrl,
                                Type = "Meta Refresh Redirect"
                            });
                            currentUrl = metaRedirectUrl;
                            redirectsCount++;
                            continue;
                        }

                        // JavaScript Redirect
                        string jsRedirectUrl = GetJavaScriptRedirectUrl(driver.PageSource);
                        if (!string.IsNullOrEmpty(jsRedirectUrl))
                        {
                            redirectResults.Add(new RedirectResult
                            {
                                StatusCode = "200",
                                Url = jsRedirectUrl,
                                Type = "JavaScript Redirect"
                            });
                            currentUrl = jsRedirectUrl;
                            redirectsCount++;
                            continue;
                        }

                        break;
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"HTTP Error: {ex.Message}");
            }
            catch (WebDriverException ex)
            {
                Console.WriteLine($"Browser Error: {ex.Message}");
            }

            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        return redirectResults;
    }

    static string FormatColumn(string content, int width)
    {
        if (content.Length > width)
            return content.Substring(0, width - 3) + "...";
        return content.PadRight(width);
    }

    public class RedirectResult
    {
        public string StatusCode { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    static string GetEmbeddedRedirectUrl(string url)
    {
        Uri uri;
        if (Uri.TryCreate(url, UriKind.Absolute, out uri))
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            foreach (var key in query.AllKeys)
            {
                if (Uri.TryCreate(query[key], UriKind.Absolute, out Uri embeddedUri))
                {
                    return embeddedUri.ToString();
                }
            }
        }
        return null;
    }

    static string GetMetaRefreshRedirectUrl(string pageSource)
    {
        //string pattern = @"<meta\s+http-equiv=['""]?refresh['""]?\s+content=['""]?.*?['""]?.*?>";
        //MatchCollection matches = Regex.Matches(pageSource, pattern, RegexOptions.IgnoreCase);

        int metaIndex = pageSource.IndexOf("http-equiv=\"refresh\"", StringComparison.OrdinalIgnoreCase);
        if (metaIndex >= 0)
        {
            int urlStartIndex = pageSource.IndexOf("URL=", metaIndex, StringComparison.OrdinalIgnoreCase) + 4;
            if (urlStartIndex > 4)
            {
                int urlEndIndex = pageSource.IndexOfAny(new[] { '"', '\'', '>' }, urlStartIndex);
                if (urlEndIndex > urlStartIndex)
                    return pageSource.Substring(urlStartIndex, urlEndIndex - urlStartIndex).Trim();
            }
        }
        return null;
    }

    static string GetJavaScriptRedirectUrl(string pageSource)
    {
        int jsIndex = pageSource.IndexOf("window.location", StringComparison.OrdinalIgnoreCase);
        if (jsIndex >= 0)
        {
            int urlStartIndex = pageSource.IndexOfAny(new[] { '"', '\'' }, jsIndex) + 1;
            int urlEndIndex = pageSource.IndexOfAny(new[] { '"', '\'' }, urlStartIndex);
            if (urlStartIndex > 0 && urlEndIndex > urlStartIndex)
                return pageSource.Substring(urlStartIndex, urlEndIndex - urlStartIndex).Trim();
        }
        return null;
    }
}
