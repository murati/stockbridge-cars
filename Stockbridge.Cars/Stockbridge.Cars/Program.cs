using CefSharp;
using CefSharp.DevTools.Page;
using CefSharp.OffScreen;
using HtmlAgilityPack;
using Stockbridge.Cars.Context;
using Stockbridge.Cars.Handlers;
using StockbridgeFinancials.Models.DataModels;
using StockbridgeFinancials.Models.ScriptingModels;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.Text.Json;
using System.Xml;

namespace Stockbridge.Cars // Note: actual namespace depends on the project name.
{
    internal class Program
    {


        private static Dictionary<int, string> navigationToSucceed = new Dictionary<int, string> { { 1, "Login Completed" }, { 2, "1st search results" }, { 3, "2nd search results" }, { 4, "Home delivery results" }, { 5, "Home Delv plus Model X 1st results" }, { 6, "Home Delv plus Model X 2nd results" } };
        private static Dictionary<int, string> navigationToFail = new Dictionary<int, string> { { 1, "Login Failed" }, { 2, "Unable to make search" }, { 3, "Unable to move forward" }, { 4, "Unable to check home delivery" }, { 5, "Unable to search Model X" }, { 6, "Unable to move enchanced results" } };

        static async Task Main(string[] args)
        {
            CreateFolder("ScreenShots");
            CreateFolder("Outputs");
            try
            {
                AsyncContext.Run(async delegate
                {
                    Cef.EnableWaitForBrowsersToClose();
                    var settings = new CefSettings { CachePath = Path.GetFullPath("cache") };
                    var success = await Cef.InitializeAsync(settings);
                    if (!success)
                        return;
                    var scriptsToExecute = CarsScripting.InitializeCarsScripting();

                    await ScrapeCarsDotCom(scriptsToExecute);
                });

            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error has occured. See details:\n{ex.Message}\n{ex.StackTrace}");
                BreakExecution();
            }
            Cef.WaitForBrowsersToClose();
            Cef.ShutdownWithoutChecks();

        }

        private static void CreateFolder(string folderName)
        {
            try
            {
                if (!Directory.Exists(folderName))
                    Directory.CreateDirectory(folderName);

            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"The application could not create folder named {folderName}. Please create the folder under {Path.GetFullPath(Assembly.GetExecutingAssembly().Location)} to continue");
                Console.WriteLine(ex.Message);
                Console.ForegroundColor = ConsoleColor.White;

                throw;
            }
        }

        private static async Task ScrapeCarsDotCom(List<CarsScripting> carsScriptings)
        {
            //Reduce rendering speed to one frame per second so it's easier to take screen shots
            var browserSettings = new BrowserSettings { WindowlessFrameRate = 1 };
            var requestContextSettings = new RequestContextSettings { CachePath = Path.GetFullPath("cache") };

            // RequestContext can be shared between browser instances and allows for custom settings
            // e.g. CachePath
            using (var requestContext = new RequestContext(requestContextSettings))
            using (var browser = new ChromiumWebBrowser("https://www.cars.com/", browserSettings, requestContext))
            {

                await browser.WaitForInitialLoadAsync();
                //browser.RenderProcessMessageHandler = new RenderProcessMessageHandler();

                //Check preferences on the CEF UI Thread
                await Cef.UIThreadTaskFactory.StartNew(delegate
                {
                    var preferences = requestContext.GetAllPreferences(true);

                    //Check do not track status
                    var doNotTrack = (bool)preferences["enable_do_not_track"];

                    Debug.WriteLine("DoNotTrack: " + doNotTrack);
                });
                var onUi = Cef.CurrentlyOnThread(CefThreadIds.TID_UI);

                var contentSize = await browser.GetContentSizeAsync();
                var viewport = new Viewport
                {
                    Height = contentSize.Height,
                    Width = contentSize.Width,
                    Scale = 1.0
                };

                //execute the JS in the given order
                JavascriptResponse scriptResult;
                WaitForNavigationAsyncResponse navResult;
                int navigationIndex = 1;
                List<string> results = new List<string>();
                var dataScraping = CarsScripting.InitializeScrapeScripting();
                for (int i = 0; i < carsScriptings.Count; i++)
                {
                    var scripting = carsScriptings.ElementAt(i);
                    await Task.Delay(1000); // Following lines throws exception all the time. Delay() lowers the probability of throwing exception
                    //if (!string.IsNullOrEmpty(scripting.Selector) && !scripting.IsNavigation)
                    //    await browser.WaitForSelectorAsync(scripting.Selector);
                    scriptResult = await browser.EvaluateScriptAsync(scripting.Script);
                    PrintJSResult(scripting.Message, scriptResult);
                    if (scriptResult.Success)
                    {
                        if (scripting.IsNavigation)
                        {
                            await Task.Delay(3000);
                            navResult = await browser.WaitForNavigationAsync();
                            PrintWaitResult(navigationIndex, navResult);
                            await browser.WaitForRenderIdleAsync();
                            if (navResult.Success)
                            {
                                await browser.WaitForInitialLoadAsync();
                                var ss = await browser.CaptureScreenshotAsync(viewport: viewport);
                                var screenshotPath = Path.Combine("ScreenShots", $"SS - {navigationToSucceed[navigationIndex]} - {DateTime.Now.Ticks}.png");
                                Console.WriteLine("Screenshot ready. Saving to {0}", screenshotPath);
                                File.WriteAllBytes(screenshotPath, ss);
                                Console.WriteLine("Screenshot ready. Saving to {0}", navigationToSucceed[navigationIndex]);
                                navigationIndex++;
                                if (scripting.IsResultsPage)
                                {
                                    var source = await browser.GetSourceAsync();
                                    if (source.Contains("vehicle-card-"))
                                        results.Add(source);

                                }
                            }
                            else
                            {
                                BreakExecution();
                                break;
                            }
                        }
                    }
                    else
                    {
                        BreakExecution();
                        break;
                    };

                }

                GenerateOutputs(results);
            }
        }

        private static void BreakExecution()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Failed to execute designed flow! Please try again later...");
            Console.ForegroundColor = ConsoleColor.White;
            Console.ReadLine();
        }

        private static void GenerateOutputs(List<string> results)
        {
            HtmlNode dataNode;
            VehicleModel vehicle;
            List<VehicleModel> vehicles = new();

            try
            {
                for (int i = 0; i < results.Count(); i++)
                {
                    HtmlDocument document = new HtmlDocument();
                    document.LoadHtml(results[i]);
                    var vehicleCards = document.DocumentNode.SelectNodes("//div[contains(@id,'vehicle-card-')]");
                    for (int j = 0; j < vehicleCards.Count(); j++)
                    {
                        var vehicleCard = vehicleCards.ElementAt(j);
                        vehicle = new();
                        if (j < vehicleCard.SelectNodes("//h2[@class='title']").Count)
                        {
                            dataNode = vehicleCard.SelectNodes("//h2[@class='title']").ElementAt(j);
                            vehicle.Title = dataNode != null ? dataNode.InnerText.ClearText() : "";
                        }
                        if (j < vehicleCard.SelectNodes("//div[@class='mileage']").Count)
                        {
                            dataNode = vehicleCard.SelectNodes("//div[@class='mileage']").ElementAt(j);
                            vehicle.Mileage = dataNode != null ? dataNode.InnerText.ClearText() : "";
                        }
                        if (j < vehicleCard.SelectNodes("//span[@class='primary-price']").Count)
                        {
                            dataNode = vehicleCard.SelectNodes("//span[@class='primary-price']").ElementAt(j);
                            vehicle.Price = dataNode != null ? dataNode.InnerText.ClearText() : "";
                        }
                        if (j < vehicleCard.SelectNodes("//div[@class='dealer-name']").Count)
                        {
                            dataNode = vehicleCard.SelectNodes("//div[@class='dealer-name']").ElementAt(j);
                            vehicle.Dealer = dataNode != null ? dataNode.InnerText.ClearText() : "";
                        }
                        if (j < vehicleCard.SelectNodes("//div[contains(@class,'miles-from ')]").Count)
                        {
                            dataNode = vehicleCard.SelectNodes("//div[contains(@class,'miles-from ')]").ElementAt(j);
                            vehicle.Distance = dataNode != null ? dataNode.InnerText.ClearText() : "";
                        }
                        vehicles.Add(vehicle);

                    }
                    string outputPath = Path.Combine("Outputs", $"Results - {navigationToSucceed[i + 2]} - {DateTime.Now.Ticks}.json");
                    File.WriteAllText(outputPath, JsonSerializer.Serialize(vehicles));
                    int randomIndex = new Random().Next(0, vehicles.Count - 1);
                    Console.ForegroundColor = ConsoleColor.Blue;
                    vehicle = vehicles.ElementAt(randomIndex);
                    Console.WriteLine($"Car is :{vehicle.Title}\nPrice tag :{vehicle.Price} @ {vehicle.Mileage}\nDealer is :{vehicle.Dealer} and total distance to current location : {vehicle.Distance}");
                    Console.WriteLine("-----------------------------------");
                    Console.ForegroundColor = ConsoleColor.White;
                    vehicles.Clear();

                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                Console.ForegroundColor = ConsoleColor.White;
            }
        }


        private static void PrintJSResult(string message, JavascriptResponse clickResult)
        {
            Console.ForegroundColor = clickResult.Success ? ConsoleColor.Green : ConsoleColor.Red;
            if (clickResult.Success)
                Console.WriteLine($"{message} is successful");
            else
                Console.WriteLine($"{message} is unsuccessful");
            Console.ForegroundColor = ConsoleColor.White;
        }
        private static void PrintWaitResult(int navigationIndex, WaitForNavigationAsyncResponse response)
        {
            Console.ForegroundColor = response.Success ? ConsoleColor.Cyan : ConsoleColor.Magenta;
            if (response.Success)
                Console.WriteLine($"{navigationToSucceed[navigationIndex]} is succeeded : {response.Success}\nHttpCode : {response.HttpStatusCode}");
            else Console.WriteLine($"{navigationToFail[navigationIndex]} is failed : {response.Success}\nHttpCode : {response.HttpStatusCode}");
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}