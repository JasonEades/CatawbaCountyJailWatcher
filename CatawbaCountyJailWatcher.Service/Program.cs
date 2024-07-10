using CatawbaCountyJailWatcher.Service;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddWindowsService();
        services.AddHostedService<Worker>();
        services.AddMemoryCache();
    })
    .Build();


host.Run();
