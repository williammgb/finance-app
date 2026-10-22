using FinanceApp.Components;
using FinanceApp.Services;
using FinanceApp.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var contentRoot = builder.Environment.ContentRootPath;
var dataFolder = builder.Configuration["DataFolder"] ?? "data";

// The database is <content root>/<DataFolder>/finance.db; DataFolder may be an absolute path.
// Demo mode (--Demo true) uses <DataFolder>/demo.db instead, rebuilt from the committed sample at
// every start; finance.db is never opened then.
string connectionString;
CsvData? demoData = null;
if (builder.Configuration.GetValue<bool>("Demo"))
{
    var samplePath = Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "samples", "example-spending.csv"));
    if (!File.Exists(samplePath))
    {
        Console.Error.WriteLine($"Demo data not found: {samplePath}");
        return 1;
    }

    var read = CsvFormat.Read(CsvFormat.Decode(File.ReadAllBytes(samplePath)));
    if (!read.Ok)
    {
        Console.Error.WriteLine($"Demo data not readable: {samplePath}: {read.Errors[0]}");
        return 1;
    }

    demoData = read.Data;
    var folder = Path.Combine(contentRoot, dataFolder);
    Directory.CreateDirectory(folder);
    var demoFile = Path.Combine(folder, "demo.db");
    foreach (var file in new[] { demoFile, demoFile + "-shm", demoFile + "-wal" })
    {
        File.Delete(file);
    }

    connectionString = new SqliteConnectionStringBuilder { DataSource = demoFile }.ToString();
}
else
{
    connectionString = FinanceDb.PrepareFile(contentRoot, dataFolder);
}

builder.Services.AddDbContextFactory<FinanceDb>(options => options.UseSqlite(connectionString));
builder.Services.AddSingleton<CategoryService>();
builder.Services.AddSingleton<EntryService>();
builder.Services.AddSingleton<TransferService>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

using (var db = app.Services.GetRequiredService<IDbContextFactory<FinanceDb>>().CreateDbContext())
{
    db.Database.Migrate();
}

if (demoData is not null)
{
    var imported = await app.Services.GetRequiredService<TransferService>().Import(demoData);
    if (!imported.Ok)
    {
        Console.Error.WriteLine($"Demo data not imported: {imported.Error}");
        return 1;
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
return 0;
