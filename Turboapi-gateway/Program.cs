
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var proxyBuilder = builder.Services.AddReverseProxy();
proxyBuilder.LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));


builder.Services.AddCors(options =>
{
    options.AddPolicy(name: "Default",
        policy  =>
        {
            policy
                .WithOrigins("http://localhost:8080")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        });
});

var app = builder.Build();

app.MapReverseProxy();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("Default");

app.Run();

