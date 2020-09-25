// This source code is a part of project violet-server.
// Copyright (C) 2020. violet-team. Licensed under the MIT Licence.

using hsync;
using hsync.Network;
using HtmlAgilityPack;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace arcalive
{
    class Program
    {
        public static NetQueue DownloadQueue;

        static void Main(string[] args)
        {
            GCLatencyMode oldMode = GCSettings.LatencyMode;
            RuntimeHelpers.PrepareConstrainedRegions();
            GCSettings.LatencyMode = GCLatencyMode.Batch;

            ServicePointManager.DefaultConnectionLimit = int.MaxValue;

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);

            DownloadQueue = new NetQueue(800);

            Console.WriteLine("Robust Arcalive Image Downloader");
            Console.WriteLine("Copyright 2020. ");

            Console.Write("prefix> ");
            string prefix = Console.ReadLine().Trim();
            Console.Write("category> ");
            string category = Console.ReadLine().Trim();
            Console.Write("maxpage> ");
            int page = Convert.ToInt32(Console.ReadLine().Trim());

            var urls = Enumerable.Range(1, page).Select(x => $"{prefix}?category={category}&p={x}").ToList();

            Console.Write("download pages... ");
            List<string> htmls;
            {
                int complete = 0;
                using (var pb = new ExtractingProgressBar())
                {
                    htmls = NetTools.DownloadStrings(urls, "",
                    () =>
                    {
                        pb.Report(urls.Count, Interlocked.Increment(ref complete));
                    }).Result;
                }
            }

            var rr = new Regex(@"<a class=""vrow"" href=""(.*?)""");
            var subPages = new ConcurrentBag<List<string>>();
            var host = new Uri(prefix).Host;

            Parallel.ForEach(htmls, (html) =>
            {
                subPages.Add(rr.Matches(html).Cast<Match>().Select(x => $"https://{host}{x.Groups[1].Value}").ToList());
            });

            Console.WriteLine($"{subPages.Sum(x=>x.Count):#,#} page loaded");

            Console.Write("download article... ");
            var subPagesList = subPages.SelectMany(x => x).ToList();
            List<string> subHtmls;
            {
                int complete = 0;
                using (var pb = new ExtractingProgressBar())
                {
                    subHtmls = NetTools.DownloadStrings(subPagesList, "",
                    () =>
                    {
                        pb.Report(subPagesList.Count, Interlocked.Increment(ref complete));
                    }).Result;
                }
            }
            Console.WriteLine("complete");

            // url, path
            var images = new ConcurrentBag<List<(string,string)>>();

            Console.Write("extracting contents... ");
            {
                int complete = 0;
                using (var pb = new ExtractingProgressBar())
                {
                    Parallel.ForEach(subHtmls, (html) =>
                    {
                        var imgs = new List<(string, string)>();
                        var doc = new HtmlDocument();

                        doc.LoadHtml(html);
                        var id = doc.DocumentNode.SelectSingleNode("//link[@rel='canonical']").GetAttributeValue("href", "").Split('/').Last();
                        var body = doc.DocumentNode.SelectSingleNode("//div[@class='article-body']");

                        var imgnodes = body.SelectNodes(".//img");
                        if (imgnodes != null)
                        {
                            foreach (var img in imgnodes)
                            {
                                var src = img.GetAttributeValue("src", "");
                                imgs.Add(($"https:{src}?type=orig", $"download/[{id}] {imgs.Count:00}." + src.Split('.').Last()));
                            }
                        }

                        var videonodes = body.SelectNodes(".//video");
                        if (videonodes != null)
                        {
                            foreach (var video in body.SelectNodes(".//video"))
                            {
                                var src = video.GetAttributeValue("src", "");
                                if (video.GetAttributeValue("data-orig", "") == "gif")
                                    imgs.Add(($"https:{src}.gif?type=orig", $"download/[{id}] {imgs.Count:00}." + "gif"));
                                else
                                    imgs.Add(($"https:{src}?type=orig", $"download/[{id}] {imgs.Count:00}." + src.Split('.').Last()));
                            }
                        }

                        images.Add(imgs);
                        pb.Report(subHtmls.Count, Interlocked.Increment(ref complete));
                    });
                }
            }

            Console.WriteLine($"{images.Sum(x => x.Count):#,#} images loaded");

            DownloadQueue = new NetQueue(4);

            Console.Write("downloading contents... ");
            var downloadTargets = images.SelectMany(x => x).ToList();

            Directory.CreateDirectory("download");

            {
                long complete = 0;
                using (var pb = new DownloadProgressBar())
                {
                    NetTools.DownloadFiles(downloadTargets, "", (readBytes) => {
                        pb.Report(downloadTargets.Count, complete, readBytes);
                    }, () => {
                        pb.Report(downloadTargets.Count, Interlocked.Increment(ref complete), 0);
                    }).Wait();
                }
            }

            Console.WriteLine($"complete.");

            Console.ReadLine();
        }
    }
}
