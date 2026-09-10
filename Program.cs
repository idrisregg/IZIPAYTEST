using IZIPay.Data;
using IZIPay.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using IZIPay.Repos;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();
builder.Services.AddCors(options => options.AddPolicy("Front", policy =>
{
    policy
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowAnyOrigin();
}));
var connectionString = builder.Configuration.GetConnectionString("IziPay")
    ?? "Data Source=izipay.db";
var sqliteConnection = new SqliteConnectionStringBuilder(connectionString);
if (!Path.IsPathRooted(sqliteConnection.DataSource))
{
    sqliteConnection.DataSource = Path.Combine(
        builder.Environment.ContentRootPath,
        sqliteConnection.DataSource);
}

builder.Services.AddDbContext<IziPayDbContext>(options =>
    options.UseSqlite(sqliteConnection.ToString()));
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.AddHttpClient("Gateway");
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddOpenApi();

builder.Services.AddRateLimiter(s =>
{
    s.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    s.AddFixedWindowLimiter(policyName: "Fixed", op =>
    {
        op.PermitLimit = 100;
        op.Window = TimeSpan.FromMinutes(1);
        op.QueueLimit = 0;
        op.AutoReplenishment = true;
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IziPayDbContext>();
    db.Database.EnsureCreated();
    try
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE izipay ADD COLUMN checkout_id TEXT");
    }
    catch (SqliteException)
    {
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors("Front");

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

app.Run();