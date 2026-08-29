using AetherShell.Watchdog;

var builder = Host.CreateApplicationBuilder(args);

// Поддержка работы как Windows Service
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AetherShell.Watchdog";
});

builder.Services.AddHostedService<WatchdogService>();

var host = builder.Build();
host.Run();
