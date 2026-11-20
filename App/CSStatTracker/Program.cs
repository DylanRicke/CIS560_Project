using CSStatTracker.Data;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("CSStats")
    ?? throw new InvalidOperationException("Add a 'CSStats' connection string to appsettings.json.");

builder.Services.AddRazorPages();
builder.Services.AddSingleton(new Db(connectionString));
builder.Services.AddScoped<PlayerRepository>();
builder.Services.AddScoped<LookupRepository>();
builder.Services.AddScoped<MatchRepository>();
builder.Services.AddScoped<FriendRepository>();
builder.Services.AddScoped<ReportRepository>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.Run();
