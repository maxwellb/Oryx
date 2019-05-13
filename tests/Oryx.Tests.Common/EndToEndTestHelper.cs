// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Oryx.Common;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.Oryx.Tests.Common
{
    public static class EndToEndTestHelper
    {
        private const int MaxRetryCount = 10;
        private const int DelayBetweenRetriesInSeconds = 6;

        public static Task BuildRunAndAssertAppAsync(
            string appName,
            ITestOutputHelper output,
            DockerVolume volume,
            string buildCmd,
            string[] buildArgs,
            string runtimeImageName,
            int port,
            string runCmd,
            string[] runArgs,
            Func<int, Task> assertAction)
        {
            return BuildRunAndAssertAppAsync(
                appName,
                output,
                new List<DockerVolume> { volume },
                buildCmd,
                buildArgs,
                runtimeImageName,
                port,
                runCmd,
                runArgs,
                assertAction);
        }

        public static Task BuildRunAndAssertAppAsync(
            string appName,
            ITestOutputHelper output,
            List<DockerVolume> volume,
            string buildCmd,
            string[] buildArgs,
            string runtimeImageName,
            List<EnvironmentVariable> environmentVariables,
            int port,
            string runCmd,
            string[] runArgs,
            Func<int, Task> assertAction)
        {
            var AppNameEnvVariable = new EnvironmentVariable(
                LoggingConstants.AppServiceAppNameEnvironmentVariableName,
                appName);
            environmentVariables.Add(AppNameEnvVariable);
            return BuildRunAndAssertAppAsync(
                output,
                volume,
                buildCmd,
                buildArgs,
                runtimeImageName,
                environmentVariables,
                port,
                link: null,
                runCmd,
                runArgs,
                assertAction);
        }

        public static Task BuildRunAndAssertAppAsync(
            string appName,
            ITestOutputHelper output,
            List<DockerVolume> volumes,
            string buildCmd,
            string[] buildArgs,
            string runtimeImageName,
            int port,
            string runCmd,
            string[] runArgs,
            Func<int, Task> assertAction)
        {
            return BuildRunAndAssertAppAsync(
                output,
                volumes,
                buildCmd,
                buildArgs,
                runtimeImageName,
                new List<EnvironmentVariable>()
                {
                    new EnvironmentVariable(LoggingConstants.AppServiceAppNameEnvironmentVariableName, appName)
                },
                port,
                link: null,
                runCmd,
                runArgs,
                assertAction);
        }

        //  The sequence of steps are:
        //  1.  Copies the sample app from host machine's git repo to a different folder (under 'tmp/<reserved-name>' folder
        //      in CI machine and in the 'bin\debug\' folder in non-CI machine). The copying is necessary to not muck
        //      with git tracked samples folder and also a single sample could be used for verification in multiple tests.
        //  2.  Volume mounts the directory to the build image and build it.
        //  3.  Volume mounts the same directory to runtime image and runs the application.
        //  4.  A func supplied by the user is retried to the max of 10 retries between a delay of 1 second.
        public static async Task BuildRunAndAssertAppAsync(
            ITestOutputHelper output,
            List<DockerVolume> volumes,
            string buildCmd,
            string[] buildArgs,
            string runtimeImageName,
            List<EnvironmentVariable> environmentVariables,
            int port,
            string link,
            string runCmd,
            string[] runArgs,
            Func<int, Task> assertAction)
        {
            var dockerCli = new DockerCli();

            // Build
            var buildAppResult = dockerCli.Run(new DockerRunArguments
            {
                ImageId = Settings.BuildImageName,
                EnvironmentVariables = environmentVariables,
                Volumes = volumes,
                RunContainerInBackground = false,
                CommandToExecuteOnRun = buildCmd,
                CommandArguments = buildArgs,
            });

            await RunAssertsAsync(
               () =>
               {
                   Assert.True(buildAppResult.IsSuccess);
                   return Task.CompletedTask;
               },
               buildAppResult,
               output);

            // Run
            await RunAndAssertAppAsync(
                runtimeImageName,
                output,
                volumes,
                environmentVariables,
                port,
                link,
                runCmd,
                runArgs,
                assertAction,
                dockerCli);
        }

        public static async Task RunAndAssertAppAsync(
            string imageName,
            ITestOutputHelper output,
            List<DockerVolume> volumes,
            List<EnvironmentVariable> environmentVariables,
            int port,
            string link,
            string runCmd,
            string[] runArgs,
            Func<int, Task> assertAction,
            DockerCli dockerCli)
        {
            DockerRunCommandProcessResult runResult = null;
            var showDebugInfo = true;
            try
            {
                // Docker run the runtime container as a foreground process. This way we can catch any errors
                // that might occur when the application is being started.
                runResult = dockerCli.RunAndDoNotWaitForProcessExit(new DockerRunArguments
                {
                    ImageId = imageName,
                    EnvironmentVariables = environmentVariables,
                    Volumes = volumes,
                    PortInContainer = port,
                    Link = link,
                    CommandToExecuteOnRun = runCmd,
                    CommandArguments = runArgs
                });

                // An exception could have occurred when a Docker process failed to start.
                Assert.Null(runResult.Exception);
                Assert.False(runResult.Process.HasExited);

                // Example output that is ouput when "docker port <container-name>" is run: 
                // 6379/tcp -> 0.0.0.0:32774
                var getPortMappingResult = dockerCli.GetPortMapping(runResult.ContainerName, port);
                Assert.Null(getPortMappingResult.Exception);
                var stdOut = getPortMappingResult.StdOut;
                stdOut = stdOut?.Trim();
                var mappingParts = stdOut.Split(":");
                Assert.Equal(2, mappingParts.Length);
                var hostPort = Convert.ToInt32(mappingParts[1]);

                for (var i = 0; i < MaxRetryCount; i++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(DelayBetweenRetriesInSeconds));

                    try
                    {
                        // Make sure the process is still alive and fail fast if not.
                        Assert.False(runResult.Process.HasExited);
                        await assertAction(hostPort);

                        showDebugInfo = false;
                        break;
                    }
                    catch (Exception ex) when (ex.InnerException is IOException ||
                    ex.InnerException is SocketException)
                    {
                        if (i == MaxRetryCount - 1)
                        {
                            throw;
                        }
                    }
                }
            }
            finally
            {
                if (runResult != null)
                {
                    // Stop the container so that shared resources (like ports) are disposed.
                    dockerCli.StopContainer(runResult.ContainerName);

                    // Access the output and error streams after the process has exited
                    if (showDebugInfo)
                    {
                        output.WriteLine(runResult.GetDebugInfo());
                    }
                }
            }
        }

        private static async Task RunAssertsAsync(Func<Task> action, DockerResultBase res, ITestOutputHelper output)
        {
            try
            {
                await action();
            }
            catch (EqualException exc)
            {
                output.WriteLine(res.GetDebugInfo(new Dictionary<string, string>
                {
                    { "Actual value", exc.Actual },
                    { "Expected value", exc.Expected },
                }));
                throw;
            }
            catch (Exception)
            {
                output.WriteLine(res.GetDebugInfo());
                throw;
            }
        }
    }
}