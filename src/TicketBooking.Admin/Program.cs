
using Amazon.Runtime;
using Amazon.DynamoDBv2;
using MudBlazor.Services;
using TicketBooking.Admin.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = true; // Permite ver o erro real no log do servidor
    });

builder.Services.AddMudServices();

var awsOptions = builder.Configuration.GetAWSOptions();
if (builder.Environment.IsDevelopment())
{
    awsOptions.Credentials = new BasicAWSCredentials("test", "test");
}
builder.Services.AddDefaultAWSOptions(awsOptions);
builder.Services.AddAWSService<IAmazonDynamoDB>();
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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
