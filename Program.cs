using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace WindowzHandles
{
    internal class Program
    {
        static void Main()
        {
            string strUrl = "https://falconnest.solano.edu";
            string strUsername = "gking8";
            string strPassword = "EXEU9U";

            ChromeOptions objOptions = new ChromeOptions();
            objOptions.AddArgument("--log-level=3");
            objOptions.AddExcludedArgument("enable-logging");

            ChromeDriverService objService = ChromeDriverService.CreateDefaultService();
            objService.SuppressInitialDiagnosticInformation = true;
            objService.HideCommandPromptWindow = true;

            using (IWebDriver objDriver = new ChromeDriver(objService, objOptions))
            {
                objDriver.Navigate().GoToUrl(strUrl);
                Thread.Sleep(500);

                objDriver.FindElement(By.Id("usernameUserInput")).SendKeys(strUsername);
                objDriver.FindElement(By.Id("password")).SendKeys(strPassword);
                objDriver.FindElement(By.CssSelector("button[type='submit']")).Click();
                Thread.Sleep(500);

                objDriver.Navigate().GoToUrl("http://solano.instructure.com/");
                Thread.Sleep(1000);

                List<(string strName, string strUrl)> lstCourseList = new List<(string, string)>();
                var colCourseElements = objDriver.FindElements(By.CssSelector("div.ic-DashboardCard__box .ic-DashboardCard"));
                foreach (var objCourseElement in colCourseElements)
                {
                    try
                    {
                        var objLink = objCourseElement.FindElement(By.CssSelector("a"));
                        string strCourseName = objLink.Text.Trim();
                        string strCourseUrl = objLink.GetAttribute("href");
                        if (!string.IsNullOrEmpty(strCourseName) && !string.IsNullOrEmpty(strCourseUrl))
                            lstCourseList.Add((strCourseName, strCourseUrl));
                    }
                    catch { }
                }

                if (lstCourseList.Count == 0)
                {
                    Console.WriteLine("No courses found.");
                    return;
                }

                Console.WriteLine("Available courses:");
                for (int intI = 0; intI < lstCourseList.Count; intI++)
                    Console.WriteLine($"{intI + 1}: {lstCourseList[intI].strName}");

                Console.Write("Select a course by entering its number: ");
                int intSelected = 0;
                while (!int.TryParse(Console.ReadLine(), out intSelected) || intSelected < 1 || intSelected > lstCourseList.Count)
                    Console.Write("Invalid input. Please enter a valid course number: ");

                string strSelectedUrl = lstCourseList[intSelected - 1].strUrl;
                objDriver.Navigate().GoToUrl(strSelectedUrl);
                Console.WriteLine($"Navigated to: {lstCourseList[intSelected - 1].strName}");

                Console.Write("Browse by (1) Assignments or (2) Modules? Enter 1 or 2: ");
                int intBrowseChoice = 0;
                while (!int.TryParse(Console.ReadLine(), out intBrowseChoice) || (intBrowseChoice != 1 && intBrowseChoice != 2))
                    Console.Write("Invalid input. Please enter 1 for Assignments or 2 for Modules: ");

                if (intBrowseChoice == 2)
                {
                    objDriver.Navigate().GoToUrl(strSelectedUrl + "/modules");

                    WebDriverWait objWait = new WebDriverWait(objDriver, TimeSpan.FromSeconds(10));
                    objWait.Until(d => d.FindElements(By.CssSelector("div.context_module")).Count > 0);
                    Thread.Sleep(2000);

                    var colModules = objDriver.FindElements(By.CssSelector("div.context_module"));

                    List<(string strTitle, string strType, string strName, string strLink)> lstModuleItems = new List<(string, string, string, string)>();

                    foreach (var objModule in colModules)
                    {
                        try
                        {
                            var objModuleTitleElem = objModule.FindElement(By.CssSelector("h2 span.name"));
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine(objModuleTitleElem.Text.Trim());
                            Console.ResetColor();
                        }
                        catch
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("Unnamed Module");
                            Console.ResetColor();
                        }

                        var colItems = objModule.FindElements(By.CssSelector("li.context_module_item"));
                        Console.WriteLine($"    Found {colItems.Count} items");

                        foreach (var objItem in colItems)
                        {
                            string strType = "Unknown";
                            try
                            {
                                var objIcon = objItem.FindElement(By.CssSelector("span.type_icon"));
                                strType = objIcon.GetAttribute("title")?.Trim() ?? "Unknown";
                            }
                            catch { }

                            string strName = "Unnamed Item";
                            string strLinkUrl = "";
                            try
                            {
                                var objNameSpan = objItem.FindElement(By.CssSelector("span.item_name"));
                                strName = objNameSpan.Text.Trim();

                                var objLink = objNameSpan.FindElements(By.CssSelector("a")).FirstOrDefault();
                                if (objLink != null)
                                {
                                    strName = objLink.Text.Trim();
                                    strLinkUrl = objLink.GetAttribute("href")?.Trim() ?? "";
                                }
                            }
                            catch { }

                            Console.ForegroundColor = ConsoleColor.Magenta;
                            Console.WriteLine($"    {strType}");
                            Console.ResetColor();

                            Console.WriteLine($"    {strName}");

                            if (!string.IsNullOrWhiteSpace(strLinkUrl))
                            {
                                Console.ForegroundColor = ConsoleColor.Blue;
                                Console.WriteLine($"    Link: {strLinkUrl}");
                                Console.ResetColor();

                                lstModuleItems.Add((strName, strType, strName, strLinkUrl));
                            }
                        }

                        Console.WriteLine();
                    }

                    Console.WriteLine("\nRetrieving and summarizing content from module item links...\n");

                    foreach (var objItem in lstModuleItems)
                    {
                        try
                        {
                            objDriver.Navigate().GoToUrl(objItem.strLink);
                            Thread.Sleep(2000);

                            string strPageText = Regex.Replace(objDriver.PageSource, "<[^>]*>", string.Empty);
                            string strContent = strPageText.Length > 3000 ? strPageText.Substring(0, 3000) : strPageText;

                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"{objItem.strName} - Summary:");
                            Console.ResetColor();
                            Console.WriteLine(strContent.Substring(0, Math.Min(strContent.Length, 500)) + "...\n");
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"Failed to summarize {objItem.strName}: {ex.Message}");
                            Console.ResetColor();
                        }
                    }

                    Console.WriteLine("end of program, pausing 10s");
                    Thread.Sleep(10000);
                }
            }
        }
    }
}
