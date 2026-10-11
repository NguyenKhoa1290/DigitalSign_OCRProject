using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Blazored.LocalStorage;
using Blazored.Toast;
using Microsoft.AspNetCore.Components.Authorization;
using HauDocumentApp;
using HauDocumentApp.Auth;
using HauDocumentApp.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Docker can use the current browser origin; local dotnet development keeps its configured Gateway URL.
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5000/";
if (apiBaseUrl == "/")
    apiBaseUrl = builder.HostEnvironment.BaseAddress;
if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiBaseUri) ||
    apiBaseUri.Scheme is not ("http" or "https"))
    throw new InvalidOperationException("ApiBaseUrl must be an absolute HTTP(S) URL.");

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = apiBaseUri
});

// Auth & Services
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddBlazoredToast();
builder.Services.AddAuthorizationCore();

builder.Services.AddScoped<CustomAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(
    p => p.GetRequiredService<CustomAuthStateProvider>());

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ApiService>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<SignatureService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<MonitoringService>();

await builder.Build().RunAsync();
