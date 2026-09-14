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

// HTTP Client — Blazor WASM pattern
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri("http://localhost:5000/")
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

await builder.Build().RunAsync();
