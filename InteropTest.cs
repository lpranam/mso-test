﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using excel = Microsoft.Office.Interop.Excel;
using powerPoint = Microsoft.Office.Interop.PowerPoint;
using word = Microsoft.Office.Interop.Word;

namespace mso_test
{
    internal class InteropTest
    {
        public static word.Application wordApp;
        public static excel.Application excelApp;
        public static powerPoint.Application powerPointApp;

        public static void startApplication(string application)
        {
            if (application == "word")
            {
                wordApp = new word.Application();
                wordApp.DisplayAlerts = word.WdAlertLevel.wdAlertsNone;
                wordApp.AutomationSecurity = Microsoft.Office.Core.MsoAutomationSecurity.msoAutomationSecurityForceDisable;
            }

            if (application == "excel")
            {
                excelApp = new excel.Application();
                excelApp.DisplayAlerts = false;
                excelApp.AutomationSecurity = Microsoft.Office.Core.MsoAutomationSecurity.msoAutomationSecurityForceDisable;
            }

            if (application == "powerpoint")
            {
                powerPointApp = new powerPoint.Application();
                powerPointApp.DisplayAlerts = powerPoint.PpAlertLevel.ppAlertsNone;
                powerPointApp.AutomationSecurity = Microsoft.Office.Core.MsoAutomationSecurity.msoAutomationSecurityForceDisable;
            }
        }

        public static void quitApplication(string application)
        {
            try
            {
                if (application == "word")
                    wordApp.Quit();

                if (application == "excel")
                    excelApp.Quit();

                if (application == "powerpoint")
                    powerPointApp.Quit();
            }
            catch
            {
                if (application == "word")
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(wordApp);

                if (application == "excel")
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(excelApp);

                if (application == "powerpoint")
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(powerPointApp);
            }
            wordApp = null;
            excelApp = null;
            powerPointApp = null;
        }

        public static void restartApplication(string application)
        {
            Console.WriteLine("Restarting application");
            quitApplication(application);
            startApplication(application);
        }

        public static HttpClient coolClient = new HttpClient() { BaseAddress = new Uri("https://staging.eu.collaboraonline.com/cool/convert-to") };

        public static HashSet<string> allowedExtension = new HashSet<string>()
        {
            ".odt",
            ".docx",
            ".doc",
            ".ods",
            ".xls",
            ".xlsx",
            ".odp",
            ".ppt",
            ".pptx"
        };

        //args can be word/excel/powerpoint
        //specified appropriate docs will be tested with the specified program
        private static async Task Main(string[] args)
        {
            ServicePointManager.Expect100Continue = false;

            if (args.Length <= 0)
            {
                quitApplication(args[0]);
                return;
            }

            startApplication(args[0]);
            await testDownloadedfiles(args[0]);

            quitApplication(args[0]);
        }

        public static async Task<(bool, string)> testFile(string application, string fileName)
        {
            if (File.Exists(fileName + ".failed"))
                return (false, "");

            if (application == "word")
            {
                return TestWordDoc(fileName);
            }
            else if (application == "excel")
            {
                return TestExcelWorkbook(fileName);
            }
            else if (application == "powerpoint")
            {
                return TestPowerPointPresentation(fileName);
            }

            return (false, "");
        }

        public static void logInfo(string application, string failType, string msg)
        {
            string fileName = (failType == "conversion" ? "failed_conversion_" : "failed_files_") + application + ".txt";

            using (FileStream fs = new FileStream(fileName, FileMode.Append))
            {
                byte[] info = new UTF8Encoding(true).GetBytes(msg + "\n");
                fs.Write(info, 0, info.Length);
            }
        }

        public static async Task testDirectoriy(string application, DirectoryInfo dir, string convertTo)
        {
            FileInfo[] fileInfos = dir.GetFiles();
            var watch = new System.Diagnostics.Stopwatch();
            foreach (FileInfo file in fileInfos)
            {
                if (!allowedExtension.Contains(file.Extension) ||
                    File.Exists(Path.GetFullPath(@"converted\" + convertTo + @"\" + Path.GetFileNameWithoutExtension(file.Name) + "." + convertTo)) ||
                    File.Exists(Path.GetFullPath(@"converted\" + convertTo + @"\" + Path.GetFileNameWithoutExtension(file.Name) + "." + convertTo + ".failed")))
                    continue;

                Console.WriteLine("Starting test for " + file.Name);
                watch.Restart();
                Task<(bool, string)> DownloadResultTask = Task.Run(() => testFile(application, file.FullName));
                if (!DownloadResultTask.Wait(180000))
                {
                    Console.WriteLine("Testing timeout");
                    restartApplication(application);
                    try
                    {
                        System.IO.File.Move(file.FullName, file.FullName + ".timeout");
                    }
                    catch (System.IO.IOException ex) { Console.WriteLine(ex.Message); }
                    continue;
                }

                if (DownloadResultTask.Result.Item1)
                {
                    await convertFile(application, file.FullName, Path.GetFileNameWithoutExtension(file.Name), convertTo).ContinueWith(task =>
                    {
                        if (string.IsNullOrEmpty(task.Result))
                            return;
                        Task<(bool, string)> ConvertResultTask = Task.Run(() => testFile(application, task.Result));

                        if (!ConvertResultTask.Wait(180000))
                        {
                            Console.WriteLine("Testing timeout");
                            restartApplication(application);
                            try
                            {
                                System.IO.File.Move(task.Result, task.Result + ".timeout");
                            }
                            catch (System.IO.IOException ex) { Console.WriteLine(ex.Message); }
                        }
                    });
                }
                watch.Stop();
                Console.WriteLine(file.Name + $" testing took {watch.ElapsedMilliseconds} ms\n");
            }
        }

        public static async Task testDownloadedfiles(string application)
        {
            DirectoryInfo downloadedDirInfo = new DirectoryInfo(@"download");
            if (!downloadedDirInfo.Exists)
            {
                return;
            }
            DirectoryInfo[] downloadedDictInfo = downloadedDirInfo.GetDirectories();
            foreach (DirectoryInfo dict in downloadedDictInfo)
            {
                if (application == "word" && (dict.Name == "doc" || dict.Name == "docx" || dict.Name == "odt"))
                {
                    await testDirectoriy(application, dict, "docx");
                }
                else if (application == "excel" && (dict.Name == "xls" || dict.Name == "xlsx" || dict.Name == "ods"))
                {
                    await testDirectoriy(application, dict, "xlsx");
                }
                else if (application == "powerpoint" && (dict.Name == "ppt" || dict.Name == "pptx" || dict.Name == "odp"))
                {
                    await testDirectoriy(application, dict, "pptx");
                }
            }
        }

        public static async Task<string> convertFile(string application, string fullFileName, string fileName, string convertTo)
        {
            using (var request = new HttpRequestMessage(new HttpMethod("POST"), coolClient.BaseAddress + "/" + convertTo))
            {
                var multipartContent = new MultipartFormDataContent
                {
                    { new ByteArrayContent(File.ReadAllBytes(fullFileName)), "data", Path.GetFileName(fullFileName) }
                };
                request.Content = multipartContent;

                try
                {
                    using (var response = await coolClient.SendAsync(request))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            Console.Error.WriteLine("Faild to convert " + fullFileName + ": " + response.StatusCode);

                            if (response.StatusCode == HttpStatusCode.BadGateway)
                            {
                                try
                                {
                                    System.IO.File.Move(fullFileName, fullFileName + ".convfail");
                                }
                                catch (System.IO.IOException ex) { Console.WriteLine(ex.Message); }

                                logInfo(application, "conversion", fullFileName + ": " + response.StatusCode);
                            }

                            return "";
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(@"converted\" + convertTo + @"\"));

                        string convertedFilePath = @"converted\" + convertTo + @"\" + fileName + "." + convertTo;
                        using (FileStream fs = File.Open(convertedFilePath, FileMode.Create))
                        {
                            return await response.Content.CopyToAsync(fs).ContinueWith(task => { return fs.Name; });
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine(ex); }
            }
            return "";
        }

        private static (bool, string) TestWordDoc(string fileName)
        {
            word.Document doc = null;
            bool testResult = true;
            string errorMessage = "";

            try
            {
                doc = wordApp.Documents.OpenNoRepairDialog(fileName, ReadOnly: true, Visible: false, PasswordDocument: "'");
            }
            catch (COMException e)
            {
                testResult = false;
                errorMessage = e.Message;
                try
                {
                    // this statment does nothing, we just wanna make sure wordApp.Documents is valid
                    // some docs may crash the app make Documents invalid, so here it may throw error so we restart the app
                    var temp = wordApp.Documents;
                }
                catch
                {
                    restartApplication("word");
                }
            }
            if (doc != null)
            {
                try
                {
                    doc.Close(SaveChanges: false);
                }
                catch
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(doc);
                    doc = null;
                }
            }

            if (!testResult)
            {
                try
                {
                    System.IO.File.Move(fileName, fileName + ".failed");
                }
                catch (System.IO.IOException ex) { Console.WriteLine(ex.Message); }
            }

            return (testResult, errorMessage);
        }

        private static (bool, string) TestExcelWorkbook(string fileName)
        {
            excel.Workbook wb = null;
            bool testResult = true;
            string errorMessage = "";

            try
            {
                wb = excelApp.Workbooks.Open(fileName, ReadOnly: true, Password: "'", UpdateLinks: false);
            }
            catch (COMException e)
            {
                testResult = false;
                errorMessage = e.Message;
                try
                {
                    // this statment does nothing, we just wanna make sure excelApp.Workbooks is valid
                    // some docs may crash the app make Workbooks invalid, so here it may throw error so we restart the app
                    var temp = excelApp.Workbooks;
                }
                catch
                {
                    restartApplication("excel");
                }
            }

            if (wb != null)
            {
                try
                {
                    wb.Close(SaveChanges: false);
                }
                catch
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(wb);
                    wb = null;
                }
            }

            if (!testResult)
            {
                try
                {
                    System.IO.File.Move(fileName, fileName + ".failed");
                }
                catch (System.IO.IOException ex) { Console.WriteLine(ex.Message); }
            }

            return (testResult, errorMessage);
        }

        private static (bool, string) TestPowerPointPresentation(string fileName)
        {
            powerPoint.Presentation presentation = null;
            bool testResult = true;
            string errorMessage = "";

            try
            {
                presentation = powerPointApp.Presentations.Open(fileName, ReadOnly: Microsoft.Office.Core.MsoTriState.msoTrue, WithWindow: Microsoft.Office.Core.MsoTriState.msoFalse);
            }
            catch (COMException e)
            {
                testResult = false;
                errorMessage = e.Message;

                try
                {
                    // this statment does nothing, we just wanna make sure powerPointApp.Presentations is valid
                    // some docs may crash the app make Presentations invalid, so here it may throw error so we restart the app
                    var temp = powerPointApp.Presentations;
                }
                catch
                {
                    restartApplication("powerpoint");
                }
            }

            if (presentation != null)
            {
                try
                {
                    presentation.Close();
                }
                catch
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(presentation);
                    presentation = null;
                }
            }

            if (!testResult)
            {
                try
                {
                    System.IO.File.Move(fileName, fileName + ".failed");
                }
                catch (System.IO.IOException ex) { Console.WriteLine(ex.Message); }
            }

            return (testResult, errorMessage);
        }
    }
}