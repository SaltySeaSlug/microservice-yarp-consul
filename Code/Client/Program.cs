using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace Client
{
	public class Program
	{
		public static async Task Main(string[] args)
		{
			var builder = WebAssemblyHostBuilder.CreateDefault(args);
			builder.RootComponents.Add<App>("#app");

            builder.Services.AddHttpClient("FoodService", client =>
            {
                client.BaseAddress = new Uri("http://localhost/foodservice/");
            });

            builder.Services.AddHttpClient("DrinkService", client =>
            {
                client.BaseAddress = new Uri("http://localhost/drinkservice/");
            });

            await builder.Build().RunAsync();
		}
	}
}
