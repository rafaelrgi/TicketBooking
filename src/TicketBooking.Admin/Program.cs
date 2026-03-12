using MudBlazor.Services;
using TicketBooking.Admin.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Enrichers.Span;
using TicketBooking.Admin.Infra;
using TicketBooking.Domain.Settings;
using TicketBooking.Infra.Settings;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(0.04)))
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("TicketAdmin"))
        .AddAspNetCoreInstrumentation(o => o.RecordException = true)
        .AddHttpClientInstrumentation()
        .AddSource("TicketBooking.Telemetry")
        .AddOtlpExporter());

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithSpan()
    //.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{TraceId}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.OpenTelemetry(options =>
    {
        options.Endpoint = "http://localhost:4317";
        options.ResourceAttributes = new Dictionary<string, object>
        {
            ["service.name"] = "TicketBooking-API"
        };
    })
);

// Log.Logger = new LoggerConfiguration()
//     .WriteTo.Console()
//     .CreateBootstrapLogger();


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
