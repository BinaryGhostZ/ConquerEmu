using System.Diagnostics.Metrics;
using Conquer.GameServer;
using Conquer.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Register core timing and metrics services
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMetrics();

// Load WorldHost configuration
var config = new WorldHostConfig
{
    ServerName = builder.Configuration["Server:ServerName"] ?? "ConquerServer",
    ListenPort = int.TryParse(builder.Configuration["Server:GamePort"], out int gp) ? gp : 5816,
    MapGroupCount = int.TryParse(builder.Configuration["Server:MapGroupCount"], out int mg) ? mg : Math.Max(2, Environment.ProcessorCount - 2)
};
builder.Services.AddSingleton(config);

// Register hosted game server service
builder.Services.AddHostedService<GameServerWorker>();

var host = builder.Build();
await host.RunAsync();
