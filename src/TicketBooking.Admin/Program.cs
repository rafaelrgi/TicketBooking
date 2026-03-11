using MudBlazor.Services;
using TicketBooking.Admin.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Serilog;
using TicketBooking.Admin.Infra;
using TicketBooking.Domain.Settings;
using TicketBooking.Infra.Settings;

// Log.Logger = new LoggerConfiguration()
//     .WriteTo.Console()
//     .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSettings(builder.Configuration);

builder.Services.AddAuth(builder.Configuration);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => { options.DetailedErrors = true; });

builder.Services.AddMudServices();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseWebSockets();
app.UseStaticFiles();
app.UseAntiforgery();

app.UseHeaderPropagation();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/login", (string? returnUrl = "/") => Results.Challenge(new AuthenticationProperties { RedirectUri = returnUrl },
    [OpenIdConnectDefaults.AuthenticationScheme]));

app.Run();
