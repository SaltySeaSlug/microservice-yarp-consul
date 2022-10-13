using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Steeltoe.Discovery;
using Steeltoe.Discovery.Consul.Discovery;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Yarp.ReverseProxy.Transforms;

namespace Yarp.ReverseProxy.Configuration
{
	public class InMemoryConfigProvider : IProxyConfigProvider, IHostedService, IDisposable
	{
		private Timer _timer;
		private volatile InMemoryConfig _config;
		private readonly IConsulDiscoveryClient _discoveryClient;
        private List<ClusterConfig> _clusters;
        private readonly List<RouteConfig> _routes;

        public InMemoryConfigProvider(IDiscoveryClient discoveryClient)
		{
			_discoveryClient = discoveryClient as IConsulDiscoveryClient;

            _routes = GenerateRoutes();

            Update();
		}

		public IProxyConfig GetConfig() => _config;

		public Task StartAsync(CancellationToken cancellationToken)
		{
			_timer = new Timer(DoWork, null, TimeSpan.Zero,
			TimeSpan.FromSeconds(30));
			return Task.CompletedTask;
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			_timer?.Change(Timeout.Infinite, 0);
            _discoveryClient.ShutdownAsync();
			return Task.CompletedTask;
		}

        private List<RouteConfig> GenerateRoutes()
        {
            var collection = new List<RouteConfig>();
            collection.Add(new RouteConfig()
            {
                RouteId = "food-route",
                ClusterId = "food-cluster",
                AuthorizationPolicy = "Default",
                Match = new RouteMatch
                {
                    Path = "foodservice/{**catchall}"
                },
                Transforms = new List<Dictionary<string, string>>
                { 
                    new Dictionary<string, string>
                    {
                        { "PathPattern", "{**catchall}"}
                    }
                }
            }.WithTransformResponseHeader("Gateway", "YARP", true, ResponseCondition.Success));

            collection.Add(new RouteConfig()
            {
                RouteId = "drink-route",
                ClusterId = "drink-cluster",
                AuthorizationPolicy = "Default",
                Match = new RouteMatch
                {
                    Path = "drinkservice/{**catchall}"
                },
                Transforms = new List<Dictionary<string, string>>
                {
                    new Dictionary<string, string>
                    {
                        { "PathPattern", "{**catchall}"}
                    }
                }
            }.WithTransformResponseHeader("Gateway", "YARP", true, ResponseCondition.Success));

            collection.Add(new RouteConfig()
            {
                RouteId = "authentication-route-authorize",
                ClusterId = "authentication-cluster",
                AuthorizationPolicy = "Default",
                Match = new RouteMatch
                {
                    Path = "authenticationservice/{**catchall}"
                },
                Transforms = new List<Dictionary<string, string>>
                {
                    new Dictionary<string, string>
                    {
                        { "PathPattern", "api/{**catchall}"}
                    }
                }
            }.WithTransformResponseHeader("Gateway", "YARP", true, ResponseCondition.Success));

            collection.Add(new RouteConfig()
            {
                RouteId = "authentication-route",
                ClusterId = "authentication-cluster",
                Match = new RouteMatch
                {
                    Methods = new List<string> { "POST" },
                    Path = "authenticationservice/{**catchall}"
                },
                Transforms = new List<Dictionary<string, string>>
                {
                    new Dictionary<string, string>
                    {
                        { "PathPattern", "api/{**catchall}"}
                    }
                }
            }.WithTransformResponseHeader("Gateway", "YARP", true, ResponseCondition.Success));


            return collection;
        }

        private List<ClusterConfig> GenerateClusters()
        {
            List<ClusterConfig> clusters = new();

            try
            {
                var apps = _discoveryClient.GetAllInstances();

                // impliement service discovery for load balancing

                foreach (var app in apps.Reverse().Where(x => x.ServiceId != "consul"))
                {
                    var cluster = new ClusterConfig
                    {
                        ClusterId = app.ServiceId,
                        Destinations = new Dictionary<string, DestinationConfig>()
                        {
                            {
                                app.ServiceId, new DestinationConfig { Address = $"http://{app.Host}:{app.Port}" }
                            }
                        }
                    };

                    if (!clusters.Any(x => x.ClusterId == app.ServiceId))
                    {
                        clusters.Add(cluster);
                    }
                }
            }
            catch
            {

            }
            return clusters;
        }



        private void DoWork(object state)
		{
			Update();
		}

		public void Update()
		{
            _clusters = GenerateClusters();

            var oldConfig = _config;
			_config = new InMemoryConfig(_routes, _clusters);
			oldConfig?.SignalChange();
		}

		public void Dispose()
		{
			_timer?.Dispose();
            _discoveryClient.Dispose();
		}

		private class InMemoryConfig : IProxyConfig
		{
			private readonly CancellationTokenSource _cts = new CancellationTokenSource();

			public InMemoryConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
			{
				Routes = routes;
				Clusters = clusters;
				ChangeToken = new CancellationChangeToken(_cts.Token);
			}

			public IReadOnlyList<RouteConfig> Routes { get; }

			public IReadOnlyList<ClusterConfig> Clusters { get; }

			public IChangeToken ChangeToken { get; }

			internal void SignalChange()
			{
				_cts.Cancel();
			}
		}
	}
}