// Updated Program.cs for stable multithreaded summary generation
using System;
using System.Collections.Generic;
using System.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace WindowzHandles
{
    class cModuleItem
    {
        public string strName;
        public string strType;
        public string strUrl;
    }

    class Program
    {
        static string strApiKey = "";

        static void Main()
        {
            List<cModuleItem> lstModuleItems = new List<cModuleItem>();
            ChromeOptions oOptions = new ChromeOptions();
            oOptions.AddArgument("--log-level=3");
            oOptions.AddExcludedArgument("enable-logging");
            ChromeDriverService oService = ChromeDriverService.CreateDefaultService();
            oService.SuppressInitialDiagnosticInformation = true;
            oService.HideCommandPromptWindow = true;

            using (IWebDriver oDriver = new ChromeDriver(oService, oOptions))
            {
                oDriver.Navigate().GoToUrl("https://falconnest.solano.edu");
                Thread.Sleep(1000);
                oDriver.FindElement(By.Id("usernameUserInput")).SendKeys(""); //Canvas username
                oDriver.FindElement(By.Id("password")).SendKeys("");  //Canvas password inside ""'s
                oDriver.FindElement(By.CssSelector("button[type='submit']")).Click();
                Thread.Sleep(1000);

                oDriver.Navigate().GoToUrl("http://solano.instructure.com/");
                Thread.Sleep(1500);

                var lstCourses = oDriver.FindElements(By.CssSelector("div.ic-DashboardCard__box .ic-DashboardCard"));
                List<(string strName, string strUrl)> lstCourseList = new List<(string, string)>();
                foreach (var oElement in lstCourses)
                {
                    try
                    {
                        var oLink = oElement.FindElement(By.CssSelector("a"));
                        string strCourseName = oLink.Text.Trim();
                        string strCourseUrl = oLink.GetAttribute("href");
                        if (!string.IsNullOrEmpty(strCourseName))
                            lstCourseList.Add((strCourseName, strCourseUrl));
                    }
                    catch { }
                }

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Available courses:");
                Console.ResetColor();

                for (int i = 0; i < lstCourseList.Count; i++)
                    Console.WriteLine($"{i + 1}: {lstCourseList[i].strName}");

                Console.Write("Select a course by entering its number: ");
                int nSelected = int.Parse(Console.ReadLine());
                string strSelectedUrl = lstCourseList[nSelected - 1].strUrl;

                oDriver.Navigate().GoToUrl(strSelectedUrl + "/modules");
                Thread.Sleep(3000);

                var lstModules = oDriver.FindElements(By.CssSelector("div.context_module"));
                foreach (var oModule in lstModules)
                {
                    try
                    {
                        var oTitle = oModule.FindElement(By.CssSelector("h2 span.name"));
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine(oTitle.Text.Trim());
                        Console.ResetColor();
                    }
                    catch { Console.WriteLine("Unnamed Module"); }

                    var lstItems = oModule.FindElements(By.CssSelector("li.context_module_item"));
                    Console.WriteLine($"    Found {lstItems.Count} items");

                    foreach (var oItem in lstItems)
                    {
                        string strType = "Unknown";
                        string strName = "Unnamed Item";
                        string strLink = "";
                        try
                        {
                            var oIcon = oItem.FindElement(By.CssSelector("span.type_icon"));
                            strType = oIcon.GetAttribute("title")?.Trim() ?? "Unknown";
                        }
                        catch { }

                        try
                        {
                            var oSpan = oItem.FindElement(By.CssSelector("span.item_name"));
                            var oLink = oSpan.FindElements(By.CssSelector("a")).FirstOrDefault();
                            if (oLink != null)
                            {
                                strName = oLink.Text.Trim();
                                strLink = oLink.GetAttribute("href")?.Trim() ?? "";
                            }
                            else
                            {
                                strName = oSpan.Text.Trim();
                            }
                        }
                        catch { }

                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine("    " + strType);
                        Console.ResetColor();
                        Console.WriteLine("    " + strName);
                        if (!string.IsNullOrEmpty(strLink))
                        {
                            Console.ForegroundColor = ConsoleColor.Blue;
                            Console.WriteLine("    Link: " + strLink);
                            Console.ResetColor();
                        }

                        if (!string.IsNullOrEmpty(strLink) && strLink.Contains("/modules/items/"))
                            lstModuleItems.Add(new cModuleItem { strName = strName, strType = strType, strUrl = strLink });
                    }
                }
            }

            Console.WriteLine("\nRetrieving and summarizing content from module item links...\n");
            SemaphoreSlim oSemaphore = new SemaphoreSlim(3); // limit concurrency
            List<Task> lstTasks = new List<Task>();

            foreach (var oItem in lstModuleItems)
            {
                lstTasks.Add(Task.Run(async () =>
                {
                    await oSemaphore.WaitAsync();
                    try
                    {
                        ChromeOptions oOpt = new ChromeOptions();
                        oOpt.AddArgument("--headless");
                        oOpt.AddArgument("--log-level=3");
                        ChromeDriverService oSvc = ChromeDriverService.CreateDefaultService();
                        oSvc.HideCommandPromptWindow = true;

                        using (IWebDriver oDrv = new ChromeDriver(oSvc, oOpt))
                        {
                            oDrv.Navigate().GoToUrl(oItem.strUrl);
                            Thread.Sleep(2000);

                            string strPageText = oDrv.FindElement(By.TagName("body")).Text;
                            string strSummary = await fQueryChatGptAsync(strPageText);

                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"{oItem.strName} - Summary:\n{strSummary}\n");
                            Console.ResetColor();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Failed to summarize {oItem.strName}: {ex.Message}");
                        Console.ResetColor();
                    }
                    finally
                    {
                        oSemaphore.Release();
                    }
                }));
            }

            Task.WaitAll(lstTasks.ToArray());
            Console.WriteLine("\nEnd of program, pausing 10s");
            Thread.Sleep(10000);
        }

        static async Task<string> fQueryChatGptAsync(string strInput)
        {
            using (HttpClient oClient = new HttpClient())
            {
                oClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {strApiKey}");

                var oPayload = new
                {
                    model = "gpt-3.5-turbo",
                    messages = new[]
                    {
                        new { role = "system", content = "You are a helpful assistant that summarizes student content." },
                        new { role = "user", content = strInput.Substring(0, Math.Min(2000, strInput.Length)) }
                    }
                };

                string strJson = JsonConvert.SerializeObject(oPayload);
                var oResponse = await oClient.PostAsync(
                    "https://api.openai.com/v1/chat/completions",
                    new StringContent(strJson, Encoding.UTF8, "application/json")
                );

                if (!oResponse.IsSuccessStatusCode)
                    return "Error: " + oResponse.StatusCode;

                string strResult = await oResponse.Content.ReadAsStringAsync();
                dynamic oResultObj = JsonConvert.DeserializeObject(strResult);
                return (string)oResultObj.choices[0].message.content;
            }
        }
    }
}
