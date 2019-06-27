namespace Azure.Iot.Edge.Modules.SecureAccess
{
    using Azure.Iot.Edge.Modules.SecureAccess.Device;
    using Azure.Iot.Edge.Modules.SecureAccess.Module;
    using Microsoft.Extensions.DependencyInjection;

    using System;
    using System.Runtime.Loader;
    using System.Threading;
    using System.Threading.Tasks;

    internal class Program
    {
        private const string targetPortKey = "targetPort";

        private static async Task Main()
        {
            // Wait until the app unloads or is cancelled by external triggers, use it for exceptional scnearios only.
            using (var cts = new CancellationTokenSource())
            {
                AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
                Console.CancelKeyPress += (sender, cpe) => cts.Cancel();

                if (!int.TryParse(Environment.GetEnvironmentVariable(targetPortKey), out var targetPort))
                {
                    throw new ArgumentOutOfRangeException(targetPortKey, "Could not convert port number to integer.");
                }

                // Bootstrap modules and virtual devices.
                var services = new ServiceCollection();

                services.AddTransient<IDeviceHost, PassThroughDeviceHost>(isvc => 
                                    new PassThroughDeviceHost(new IotHubModuleClient(Environment.GetEnvironmentVariable("EdgeHubConnectionString")),
                                    new SecureShell(new IoTHubDeviceClient(Environment.GetEnvironmentVariable("deviceConnectionString")), 
                                    Environment.GetEnvironmentVariable("targetHost"),targetPort)));

                // Dispose method of ServiceProvider will dispose all disposable objects constructed by it as well.
                using (var serviceProvider = services.BuildServiceProvider())
                {
                    // Get a new module.
                    using (var module = serviceProvider.GetService<IDeviceHost>())
                    {
                        // Keep on looking for the new streams on IoT Hub when the previous one closes or aborts.                        
                        while (!cts.IsCancellationRequested)
                        {
                            using (var webSocket = new IoTHubClientWebSocket())
                            {
                                try
                                {
                                    using (var tcpClient = new LocalTCPClient())
                                    {
                                        // Run module
                                        Console.WriteLine("Initiating open connection...");
                                        await module.OpenConnectionAsync(webSocket, tcpClient, cts).ConfigureAwait(false);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"{ex.Message}");
                                }
                            }
                        }
                    }
                }

                await WhenCancelled(cts.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }
    }
}