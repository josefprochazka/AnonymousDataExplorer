using AnonymousDataExplorer.Components;
using AnonymousDataExplorer.Services;

var builder = WebApplication.CreateBuilder(args); // builder for web app

builder.Services.AddRazorComponents().AddInteractiveServerComponents(); // razor components + server comp
builder.Services.AddTelerikBlazor(); // telerik components

builder.Services.AddSingleton(typeof(DbProvider), DbProvider.MSSQL); // registering provider to EF ctor AppDbContext
builder.Services.AddScoped<DatabaseService>(); // registering service
builder.Services.AddDbContext<AppDbContext>(); // registering EF context for calling at beginning

var app = builder.Build(); // building of app

app.UseAntiforgery();
app.UseStaticFiles();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode(); // server support for components
app.Run();
