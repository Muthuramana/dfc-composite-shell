﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DFC.Composite.Shell.Models.Robots;
using DFC.Composite.Shell.Services.ApplicationRobot;
using DFC.Composite.Shell.Services.Paths;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace DFC.Composite.Shell.Controllers
{
    public class RobotController : BaseController
    {
        private readonly IPathService _pathService;
        private readonly ILogger<RobotController> _logger;
        private readonly IHostingEnvironment _hostingEnvironment;

        public RobotController(
            IPathService pathService,
            ILogger<RobotController> logger,
            IConfiguration configuration,
            IHostingEnvironment hostingEnvironment
            ) : base(configuration)
        {
            _pathService = pathService;
            _logger = logger;
            _hostingEnvironment = hostingEnvironment;
        }

        [HttpGet]
        public async Task<ContentResult> Robot()
        {
            try
            {
                _logger.LogInformation("Generating Robots.txt");

                var robot = GenerateThisSiteRobot();

                // get all the registered application robots.txt
                await GetApplicationRobotsAsync(robot);

                // add the Shell sitemap route
                string sitemapRouteUrl = Url.RouteUrl("Sitemap", null);

                if (sitemapRouteUrl != null)
                {
                    string shellSitemapUrl = $"{Request.Scheme}://{Request.Host}{sitemapRouteUrl}";

                    robot.Add($"{Environment.NewLine}Sitemap: {shellSitemapUrl}");
                }

                _logger.LogInformation("Generated Robots.txt");

                return Content(robot.Data, "text/plain");
            }
            catch (BrokenCircuitException ex)
            {
                _logger.LogError(ex, $"{nameof(Robot)}: BrokenCircuit: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{nameof(Robot)}: {ex.Message}");
            }

            // fall through from errors
            return Content(null, "text/plain");
        }

        private Robot GenerateThisSiteRobot()
        {
            var robot = new Robot();
            string robotsFilePath = System.IO.Path.Combine(_hostingEnvironment.WebRootPath, "StaticRobots.txt");

            if (System.IO.File.Exists(robotsFilePath))
            {
                // output the composite UI default (static) robots data from the StaticRobots.txt file
                string staticRobotsText = System.IO.File.ReadAllText(robotsFilePath);

                if (!string.IsNullOrEmpty(staticRobotsText))
                {
                    robot.Add(staticRobotsText);
                }
            }

            // add any dynamic robots data form the Shell app
            //robot.Add("<<add any dynamic text or other here>>");

            return robot;
        }

        private async Task GetApplicationRobotsAsync(Robot robot)
        {
            // loop through the registered applications and create some tasks - one per application that has a robot url
            var paths = await _pathService.GetPaths();
            var onlinePaths = paths.Where(w => w.IsOnline);

            var applicationRobotServices = await CreateApplicationRobotServiceTasksAsync(onlinePaths);

            // await all application robot service tasks to complete
            var allTasks = (from a in applicationRobotServices select a.TheTask).ToArray();

            await Task.WhenAll(allTasks);

            OutputApplicationsRobots(robot, onlinePaths, applicationRobotServices);
        }

        private async Task<List<IApplicationRobotService>> CreateApplicationRobotServiceTasksAsync(IEnumerable<Models.PathModel> paths)
        {
            // loop through the registered applications and create some tasks - one per application that has a robot url
            var applicationRobotServices = new List<IApplicationRobotService>();
            string bearerToken = await GetBearerTokenAsync();

            foreach (var path in paths.Where(w => !string.IsNullOrEmpty(w.RobotsURL)))
            {
                _logger.LogInformation($"{nameof(Action)}: Getting child robots.txt for: {path.Path}");

                var applicationRobotService = HttpContext.RequestServices.GetService(typeof(IApplicationRobotService)) as ApplicationRobotService;

                applicationRobotService.Path = path.Path;
                applicationRobotService.BearerToken = bearerToken;
                applicationRobotService.RobotsURL = path.RobotsURL;
                applicationRobotService.TheTask = applicationRobotService.GetAsync();

                applicationRobotServices.Add(applicationRobotService);
            }

            return applicationRobotServices;
        }

        private void OutputApplicationsRobots(Robot robot, IEnumerable<Models.PathModel> paths, List<IApplicationRobotService> applicationRobotServices)
        {
            string baseUrl = BaseUrl();

            // get the task results as individual sitemaps and merge into one
            foreach (var applicationRobotService in applicationRobotServices)
            {
                if (applicationRobotService.TheTask.IsCompletedSuccessfully)
                {
                    _logger.LogInformation($"{nameof(Action)}: Received child robots.txt for: {applicationRobotService.Path}");

                    var applicationRobotsText = applicationRobotService.TheTask.Result;

                    if (!string.IsNullOrEmpty(applicationRobotsText))
                    {
                        var robotsLines = applicationRobotsText.Split(Environment.NewLine);

                        for (int i = 0; i < robotsLines.Length; i++)
                        {
                            if (!string.IsNullOrEmpty(robotsLines[i]))
                            {
                                // rewrite the URL to swap any child application address prefix for the composite UI address prefix
                                foreach (var path in paths)
                                {
                                    var pathRootUri = new Uri(path.RobotsURL);
                                    string appBaseUrl = $"{pathRootUri.Scheme}://{pathRootUri.Authority}";

                                    if (robotsLines[i].StartsWith(appBaseUrl, StringComparison.InvariantCultureIgnoreCase))
                                    {
                                        robotsLines[i] = robotsLines[i].Replace(appBaseUrl, baseUrl, StringComparison.InvariantCultureIgnoreCase);
                                    }
                                }
                            }
                        }

                        robot.Add(string.Join(Environment.NewLine, robotsLines));
                    }
                }
                else
                {
                    _logger.LogError($"{nameof(Action)}: Error getting child robots.txt for: {applicationRobotService.Path}");
                }
            }
        }
    }
}
