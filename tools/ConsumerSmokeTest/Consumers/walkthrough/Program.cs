// The orders service docs/walkthrough.md follows: its host, started on a free port, sent the page's requests by
// Tour.cs. Each answer is printed as name=value, the SQL EF Core runs and the log lines as they are written, and
// the smoke test compares them with what the page states.
// sample: walkthrough/usings
using CodoMetis.TypeKit.AspNetCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog;
using Walkthrough;
// end sample

// sample: walkthrough/setup
var builder = WebApplication.CreateBuilder(args);
// end sample
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
// sample: walkthrough/setup

builder.Services.AddOpenApi(options => options.AddTypeKit());
builder.Services.AddFaultResponses<OrderFault>(fault => fault switch
{
    OrderFault.NotFound => StatusCodes.Status404NotFound,
    OrderFault.AlreadyShipped or OrderFault.Cancelled => StatusCodes.Status409Conflict,
    OrderFault.Cleared => StatusCodes.Status422UnprocessableEntity,
    _ => throw new ArgumentOutOfRangeException(nameof(fault)),
});
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.RespectRequiredConstructorParameters = true);

var connection = new SqliteConnection("Data Source=:memory:");   // the database lives as long as this connection is open
connection.Open();
builder.Services.AddDbContext<OrdersDb>(options => options.UseSqlite(connection).UseTypeKit()
    .LogTo(Console.WriteLine, [RelationalEventId.CommandExecuted]));

builder.Services.AddSingleton<Serilog.ILogger>(new LoggerConfiguration()
    .Destructure.WithTypeKit()
    .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}")
    .CreateLogger());
// end sample

// sample: walkthrough/app
var app = builder.Build();
app.UseStatusCodePages();
app.MapOpenApi();
app.MapOrders();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<OrdersDb>().Database.EnsureCreated();
    // CREATE TABLE "Orders" (
    //     "Id" TEXT NOT NULL CONSTRAINT "PK_Orders" PRIMARY KEY,
    //     "Email" TEXT NOT NULL,
    //     "Quantity" INTEGER NOT NULL,
    //     "Status" TEXT NOT NULL
    // );
}
// end sample

await app.StartAsync();
using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
await Tour.RunAsync(http, (name, value) => Console.WriteLine($"{name}={value}"));
await app.StopAsync();
connection.Dispose();
