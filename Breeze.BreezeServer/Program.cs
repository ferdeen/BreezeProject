using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Breeze.BreezeServer.Services;

namespace Breeze.BreezeServer
{
	public class Program
	{
	    private static ILogger _logger;

		public static void Main(string[] args)
		{
			var serviceProvider = new ServiceCollection()
				.AddLogging()
				.AddSingleton<ITumblerService, TumblerService>()
				.BuildServiceProvider();

			serviceProvider
				.GetService<ILoggerFactory>()
				.AddConsole(LogLevel.Debug);

			// TODO: It is messy having both a BreezeServer logger and an NTumbleBit logger
			_logger = serviceProvider.GetService<ILoggerFactory>().CreateLogger<Program>();
			
		    LogInfo("Reading Breeze server configuration");

		    // Check OS-specific default config path for the config file. Create default file if it does not exist
            string configDir = BreezeConfiguration.GetDefaultDataDir("BreezeServer");
			if (args.Contains("testnet"))
				configDir = Path.Combine(configDir, "TestNet");
			else if (args.Contains("regtest"))
			{
			    configDir = Path.Combine(configDir, "RegTest");
			}

            string configPath = Path.Combine(configDir, "breeze.conf");

		    LogInfo($"Configuration file path {configPath}");

            BreezeConfiguration config = new BreezeConfiguration(configPath);

		    LogInfo("Pre-initialising server to obtain parameters for configuration");

            var preTumblerConfig = serviceProvider.GetService<ITumblerService>();

		    LogInfo($"Tor enabled: {config.TorEnabled}.");

		    preTumblerConfig.StartTumbler(config, true, torMandatory: config.TorEnabled);

			var configurationHash = preTumblerConfig.runtime.ClassicTumblerParameters.GetHash().ToString();

		    string onionAddress;

		    if (config.TorEnabled)
		    {
		        onionAddress = preTumblerConfig.runtime.TorUri.Host.Substring(0, 16);
		        preTumblerConfig.runtime.TorConnection?.Dispose();
            }
		    else
		    {
		        onionAddress = preTumblerConfig.runtime.TumblerUris.Count > 0 ? preTumblerConfig.runtime.TumblerUris[0].Host : LocalIpAddress().ToString();
		    }

            NTumbleBit.RsaKey tumblerKey = preTumblerConfig.runtime.TumblerKey;
						
		    string regStorePath = Path.Combine(configDir, "registrationHistory.json");

		    LogInfo($"Registration history path {regStorePath}");
		    LogInfo("Checking node registration");

            BreezeRegistration registration = new BreezeRegistration();

            if (!registration.CheckBreezeRegistration(config, regStorePath, configurationHash, onionAddress, tumblerKey)) {
				_logger.LogInformation("{Time} Creating or updating node registration", DateTime.Now);
	            var regTx = registration.PerformBreezeRegistration(config, regStorePath, configurationHash, onionAddress, tumblerKey);
				if (regTx != null) {
				    LogInfo($"Submitted transaction {regTx.GetHash()} via RPC for broadcast");
                }
				else {
				    LogInfo("Unable to broadcast transaction via RPC");
                    Environment.Exit(0);
				}
			}
			else {
                LogInfo("Node registration has already been performed");
            }

		    LogInfo("Starting Tumblebit server");

            var tumbler = serviceProvider.GetService<ITumblerService>();
			
			tumbler.StartTumbler(config, false);
		}

	    private static void LogInfo(string msg)
	    {
	        _logger.LogInformation($"{DateTime.Now} {msg}");
        }

	    private static IPAddress LocalIpAddress()
	    {
	        if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) return null;

	        var host = Dns.GetHostEntry(Dns.GetHostName());

	        return host
	            .AddressList
	            .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
	    }
    }
}
