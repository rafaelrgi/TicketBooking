
using Amazon.Runtime;
using Amazon.DynamoDBv2;
using MudBlazor.Services;
using TicketBooking.Admin.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Keycloak.AuthServices.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using TicketBooking.Admin;
using TicketBooking.Admin.Infra;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuth(builder.Configuration);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = true;
    });

builder.Services.AddMudServices();

builder.Services.AddScoped(sp => new HttpClient
{
    //TODO: config
    BaseAddress = new Uri("http://localhost:5070/")
});

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

app.MapGet("/login", (string? returnUrl = "/") =>
{
    return Results.Challenge(new AuthenticationProperties { RedirectUri = returnUrl },
        [OpenIdConnectDefaults.AuthenticationScheme]);
});

app.Run();
