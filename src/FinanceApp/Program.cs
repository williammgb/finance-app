using FinanceApp.Components;
using FinanceApp.Services;
using FinanceApp.Storage;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// The database is <content root>/<DataFolder>/finance.db; DataFolder may be an absolute path.
var connectionString = FinanceDb.PrepareFile(
    builder.Environment.ContentRootPath, builder.Configuration["DataFolder"] ?? "data");
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
