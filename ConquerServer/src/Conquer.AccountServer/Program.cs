using Conquer.AccountServer;
using Conquer.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        var connectionString = hostContext.Configuration["ConnectionStrings:DefaultConnection"] ?? "Server=localhost;User=root;Password=root;Database=conquer;";
        services.AddSingleton<IAccountRepository>(new AccountRepository(connectionString));
        services.AddHostedService<AccountServerWorker>();
    })
    .Build();

await host.RunAsync();
