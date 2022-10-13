using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Steeltoe.Discovery.Client;
using Microsoft.Extensions.DependencyInjection;
using Authentication.Shared;
using Steeltoe.Extensions.Configuration;
using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using System.IO;
using LettuceEncrypt;

var builder = WebApplication.CreateBuilder(args);


//if (builder.Environment.IsProduction())
//{
//    builder.Services.AddLettuceEncrypt(config =>
//    {
//        config.AcceptTermsOfService = true;
//        config.DomainNames = new string[] { "gateway-service" };
//        config.UseStagingServer = true;
//        config.EmailAddress = "cockbainma@gmail.com";
//    }).PersistDataToDirectory(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".well-known")), null);
//}


builder.Services.AddCors();
builder.Services.AddControllers();
builder.Services.AddDiscoveryClient(builder.Configuration);
builder.Services.AddReverseProxy().LoadFromMemory();
builder.Services.AddJWTTokenAuthentication(builder.Configuration);
builder.WebHost.UseIIS();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseCors(x => x.AllowAnyHeader().AllowAnyOrigin().AllowAnyMethod());

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();


app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, ".well-known")).FullName),
    RequestPath = new PathString("/.well-known"),
    ServeUnknownFileTypes = true,
});


app.MapReverseProxy(opt =>
{
    opt.UseLoadBalancing();
    opt.UseSessionAffinity();
});






app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Map("/", () => "Hello");

app.Run();