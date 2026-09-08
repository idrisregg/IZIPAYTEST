using IZIPay.Data;
using IZIPay;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();
builder.Services.AddCors(options => options.AddPolicy("CheckoutFrontend", policy =>
{
    policy.WithOrigins("http://localhost:5500", "http://127.0.0.1:5500")
        .AllowAnyHeader()
        .AllowAnyMethod();
}));
builder.Services.AddDbContext<IziPayDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("IziPay") ?? "Data Source=izipay.db"));
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.AddHttpClient("Gateway");
builder.Services.AddScoped<PaymentService>();
builder.Services.AddOpenApi();

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

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors("CheckoutFrontend");

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
